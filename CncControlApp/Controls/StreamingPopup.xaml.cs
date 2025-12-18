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
        
        /// <summary>
        /// Cancel butonuna basıldığında tetiklenir
        /// </summary>
        public event EventHandler Cancelled;
        
        /// <summary>
        /// Sekans iptal edildi mi?
        /// </summary>
        public bool IsCancelled { get; private set; }
        
        public StreamingPopup()
        {
            InitializeComponent();
            Owner = Application.Current?.MainWindow;
            
            // ✅ Owner'ı disable etmiyoruz - bu beyazlaşmaya neden oluyor
            // Modal-like behavior yerine sadece topmost kullanıyoruz
            Topmost = true;
        }
        
        /// <summary>
        /// Probe modu: Dar popup, iptal butonu görünür
        /// </summary>
        public void ConfigureForProbe()
        {
            try
            {
                // +30% width
                SubtitleText.Width = 260;
                LiveLine.Width = 260;
                LogScroll.Width = 286;
                CancelButtonBorder.Visibility = Visibility.Visible;
            }
            catch { }
        }
        
        /// <summary>
        /// Stream/Stop modu: Geniş popup, iptal butonu gizli
        /// </summary>
        public void ConfigureForStream()
        {
            try
            {
                // +30% width
                SubtitleText.Width = 572;
                LiveLine.Width = 572;
                LogScroll.Width = 572;
                CancelButtonBorder.Visibility = Visibility.Collapsed;
            }
            catch { }
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

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsCancelled = true;
            try { App.MainController?.ClearSendBuffer(); } catch { }
            try { Cancelled?.Invoke(this, EventArgs.Empty); } catch { }
            ForceClose();
        }
    }
}