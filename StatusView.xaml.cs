using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CncControlApp
{
    public partial class StatusView : UserControl
    {
        public StatusView()
        {
            InitializeComponent();
            this.DataContext = App.MainController;
        }

        #region Log ListBox Event Handlers

        /// <summary>
        /// Klavye kısayolları (Ctrl+C, Ctrl+A) için
        /// </summary>
        private void LogListBox_KeyDown(object sender, KeyEventArgs e)
        {
            try
            {
                if (sender is ListBox listBox)
                {
                    // Ctrl+C - Kopyala
                    if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        CopySelectedLogsToClipboard();
                        e.Handled = true;
                    }
                    // Ctrl+A - Tümünü Seç
                    else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
                    {
                        listBox.SelectAll();
                        e.Handled = true;
                    }
                    // Delete - Seçili logları sil (isteğe bağlı)
                    else if (e.Key == Key.Delete)
                    {
                        DeleteSelectedLogs();
                        e.Handled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> HATA: Log klavye işlemi - {ex.Message}");
            }
        }

        /// <summary>
        /// Sağ tık context menu
        /// </summary>
        private void LogListBox_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (sender is ListBox listBox)
                {
                    // Context menu otomatik açılacak - ek işlem gerekmez
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> HATA: Log mouse right click - {ex.Message}");
            }
        }

        #endregion

        #region Context Menu Event Handlers

        /// <summary>
        /// Seçili logları panoya kopyala
        /// </summary>
        private void CopySelectedLogs_Click(object sender, RoutedEventArgs e)
        {
            CopySelectedLogsToClipboard();
        }

        /// <summary>
        /// Tüm logları seç
        /// </summary>
        private void SelectAllLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogListBox.SelectAll();
                App.MainController?.AddLogMessage($"> Tüm log mesajları seçildi ({LogListBox.Items.Count} adet)");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> HATA: Tümünü seç işlemi - {ex.Message}");
            }
        }

        /// <summary>
        /// Logları temizle
        /// </summary>
        private void ClearLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "Tüm log mesajları silinecek. Emin misiniz?",
                    "Log Temizleme Onayı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    App.MainController?.LogMessages?.Clear();
                    App.MainController?.AddLogMessage("> Log mesajları temizlendi");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> HATA: Log temizleme işlemi - {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Seçili log mesajlarını panoya kopyalar
        /// </summary>
        private void CopySelectedLogsToClipboard()
        {
            try
            {
                if (LogListBox.SelectedItems.Count > 0)
                {
                    var selectedLogs = LogListBox.SelectedItems.Cast<string>();
                    var logText = string.Join(Environment.NewLine, selectedLogs);

                    Clipboard.SetText(logText);
                    App.MainController?.AddLogMessage($"> {LogListBox.SelectedItems.Count} log mesajı panoya kopyalandı");
                }
                else
                {
                    App.MainController?.AddLogMessage("> Kopyalamak için önce log mesajları seçin");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> HATA: Panoya kopyalama işlemi - {ex.Message}");
            }
        }

        /// <summary>
        /// Seçili log mesajlarını siler
        /// </summary>
        private void DeleteSelectedLogs()
        {
            try
            {
                if (LogListBox.SelectedItems.Count > 0)
                {
                    var selectedItems = LogListBox.SelectedItems.Cast<string>().ToList();
                    foreach (var item in selectedItems)
                    {
                        App.MainController?.LogMessages?.Remove(item);
                    }
                    App.MainController?.AddLogMessage($"> {selectedItems.Count} log mesajı silindi");
                }
                else
                {
                    App.MainController?.AddLogMessage("> Silmek için önce log mesajları seçin");
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> HATA: Log silme işlemi - {ex.Message}");
            }
        }

        #endregion
    }
}