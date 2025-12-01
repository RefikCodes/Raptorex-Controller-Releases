using System;
using System.ComponentModel;
using System.Text;
using System.Windows;

namespace CncControlApp.Controls
{
    public partial class StreamingPopup : Window
    {
        private readonly StringBuilder _sb = new StringBuilder(4096);
        private bool _allowClose = false;
        
        public StreamingPopup()
        {
            InitializeComponent();
            Owner = Application.Current?.MainWindow;
            
            // ✅ Owner'ı disable etmiyoruz - bu beyazlaşmaya neden oluyor
            // Modal-like behavior yerine sadece topmost kullanıyoruz
            Topmost = true;
        }

        /// <summary>
        /// Override to prevent user from closing window manually (Alt+F4, etc.)
        /// Window can only be closed programmatically via ForceClose()
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true; // Block manual close attempts
                return;
            }
                
            base.OnClosing(e);
        }

        /// <summary>
        /// Programmatically close the window (bypasses close protection)
        /// </summary>
        public void ForceClose()
        {
            try
            {
                Dispatcher?.Invoke(() =>
                {
                    _allowClose = true;
                    Close();
                });
            }
            catch { }
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
    }
}