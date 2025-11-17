using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CncControlApp
{
    public partial class ConsoleView : UserControl
    {
        // Console'a özel bağımsız log
        public ObservableCollection<string> ConsoleEntries { get; } = new ObservableCollection<string>();

        public ConsoleView()
        {
            InitializeComponent();

            // Bindings in XAML target this
            this.DataContext = this;

            // Auto-scroll to last console entry
            ConsoleEntries.CollectionChanged += (s, e) =>
            {
                if (ConsoleListBox.Items.Count > 0)
                    ConsoleListBox.ScrollIntoView(ConsoleListBox.Items[ConsoleListBox.Items.Count - 1]);
            };
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
                    AddConsole("> ❌ Not connected – cannot send G-code");
                    return;
                }

                if (App.MainController.IsGCodeRunning)
                {
                    AddConsole("> ⏳ Execution running – manual send is disabled");
                    return;
                }

                // Çok satırlı gönderim desteği
                var lines = text.Replace("\r", "")
                                .Split('\n')
                                .Where(l => !string.IsNullOrWhiteSpace(l))
                                .ToArray();

                foreach (var line in lines)
                {
                    AddConsole($"> ▶ SEND: {line}");
                    bool ok = await App.MainController.SendGCodeCommandWithConfirmationAsync(line);
                    if (!ok)
                    {
                        AddConsole($"> ❌ FAILED: {line}");
                        break; // ilk hatada dur
                    }
                    else
                    {
                        AddConsole($"> ✅ OK: {line}");
                    }
                }

                GCodeTextBox.Clear();
                GCodeTextBox.Focus();
            }
            catch (Exception ex)
            {
                AddConsole($"> ❌ Console send error: {ex.Message}");
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
                // Makro komutlarda trailing space ekleyelim
                if (s == "G00" || s == "G01" || s == "G91" || s == "G92" || s == "G10")
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
                    case "CLEAR":
                        GCodeTextBox.Clear();
                        GCodeTextBox.Focus();
                        break;
                    case "ENTER":
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
            // (Opsiyonel) Koleksiyon boyutunu sınırla
            if (ConsoleEntries.Count > 500)
            {
                // en eskiyi düşür
                ConsoleEntries.RemoveAt(0);
            }
        }
    }
}