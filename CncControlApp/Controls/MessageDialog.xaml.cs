using System;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using System.Diagnostics; // added for stack trace

namespace CncControlApp.Controls
{
    public partial class MessageDialog : Window
    {
        // Dialog türleri (PersistentStop ekli)
        public enum DialogKind { Info, Error, Confirm, PersistentStop }

        // Global suppression flag
        public static bool SuppressErrorPopups { get; set; }

        // === Internal helper: popup trace logging ===
        private static void TracePopup(string phase, string kind, string title, string message, bool suppressed)
        {
            try
            {
                // Basit origin bilgisi (ilk dış frame)
                string origin = "unknown";
                try
                {
                    var st = new StackTrace(2, false); // skip TracePopup + caller (ShowX)
                    var frame = st.GetFrame(0);
                    if (frame != null)
                    {
                        var m = frame.GetMethod();
                        if (m != null)
                            origin = m.DeclaringType?.Name + "." + m.Name;
                    }
                }
                catch { }

                // Mesaj uzunluğu (içeriği tamamen yazmamak için)
                int len = message == null ? 0 : message.Length;
                var tag = suppressed ? "POPUP_SUPPRESSED" : "POPUP_TRACE";
                CncControlApp.App.MainController?.AddLogMessage($"> [{tag}] {phase} Kind={kind} Title='{title}' Origin={origin} MsgLen={len}");
            }
            catch { }
        }

        // Suppress popups until dispatcher idle (error spam önleme)
        public static IDisposable SuppressUntilIdle()
        {
            SuppressErrorPopups = true;
            return new RestoreOnIdle();
        }

        private sealed class RestoreOnIdle : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try
                {
                    Application.Current?.Dispatcher?.BeginInvoke(
                        new Action(() => SuppressErrorPopups = false),
                        DispatcherPriority.ApplicationIdle);
                }
                catch
                {
                    SuppressErrorPopups = false;
                }
            }
        }

        private bool _result;
        private readonly DialogKind _kind;
        private DispatcherTimer _idleWatchTimer;
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private readonly bool _autoCloseOnIdle;
        private readonly bool _useStopLabels; // Confirm için özel etiketler (STOP/Continue)

        /// <summary>
        /// Ana constructor. PersistentStop için autoCloseOnIdle kullanılabilir.
        /// </summary>
        public MessageDialog(string title, string message, DialogKind kind, bool autoCloseOnIdle = false, bool useStopLabels = false)
        {
            InitializeComponent();
            _kind = kind;
            _autoCloseOnIdle = autoCloseOnIdle;
            _useStopLabels = useStopLabels;

            Owner = Application.Current?.MainWindow;
            TitleText.Text = title;
            MessageText.Text = message;

            ConfigureVisualState(kind, message);
        }

        private void ConfigureVisualState(DialogKind kind, string message)
        {
            // Varsayılan state
            bool isConfirm = kind == DialogKind.Confirm;
            bool showOk = (kind == DialogKind.Info || kind == DialogKind.Error);
            bool isPersistent = kind == DialogKind.PersistentStop;

            if (YesButton != null) YesButton.Visibility = isConfirm ? Visibility.Visible : Visibility.Collapsed;
            if (NoButton != null)  NoButton.Visibility  = isConfirm ? Visibility.Visible : Visibility.Collapsed;
            if (OkButton != null)  OkButton.Visibility  = showOk ? Visibility.Visible : Visibility.Collapsed;

            if (ButtonsPanel != null)
                ButtonsPanel.Visibility = isPersistent ? Visibility.Collapsed : Visibility.Visible;
            if (ProgressPanel != null)
                ProgressPanel.Visibility = isPersistent ? Visibility.Visible : Visibility.Collapsed;

            if (isConfirm)
            {
                // Confirm etiketleri: varsayılan Yes/No, stop onayı için özel etiketler
                if (YesButton != null) YesButton.Content = _useStopLabels ? "STOP Run" : "Yes";
                if (NoButton != null)  NoButton.Content  = _useStopLabels ? "Continue" : "No";

                if (_useStopLabels && string.IsNullOrWhiteSpace(message))
                {
                    MessageText.Text = "İşlem güvenli şekilde duraklatıldı.\n\nSeçenekler:\n• STOP Run: Yürütmeyi tamamen durdur ve güvenli duruş sekansını çalıştır.\n• Continue: Duraklatmadan devam et (kaldığı yerden).";
                }
            }

            if (isPersistent)
            {
                // Persistent stop popup: ilerleme logları gösterilecek
                StartIdleWatcher();
                AppendProgress(message);
            }
        }

        public static bool ShowConfirm(string title, string message)
        {
            TracePopup("SHOW", "Confirm", title, message, suppressed: false);
            var dlg = new MessageDialog(title, message, DialogKind.Confirm, useStopLabels: false);
            dlg.ShowDialog();
            TracePopup("CLOSED", "Confirm", title, message, suppressed: false);
            return dlg._result;
        }

        // Stop/Pause için özel onay: STOP Run / Continue etiketleri
        public static bool ShowStopConfirm(string title, string message)
        {
            TracePopup("SHOW", "StopConfirm", title, message, suppressed: false);
            var dlg = new MessageDialog(title, message, DialogKind.Confirm, useStopLabels: true);
            dlg.ShowDialog();
            TracePopup("CLOSED", "StopConfirm", title, message, suppressed: false);
            return dlg._result;
        }

        public static void ShowInfo(string title, string message)
        {
            TracePopup("SHOW", "Info", title, message, suppressed: false);
            var dlg = new MessageDialog(title, message, DialogKind.Info);
            dlg.ShowDialog();
            TracePopup("CLOSED", "Info", title, message, suppressed: false);
        }

        public static void ShowError(string title, string message)
        {
            if (SuppressErrorPopups)
            {
                TracePopup("SUPPRESS", "Error", title, message, suppressed: true);
                try { CncControlApp.App.MainController?.AddLogMessage($"> ❌ [Suppressed Popup] {title}: {message}"); }
                catch { }
                return;
            }
            TracePopup("SHOW", "Error", title, message, suppressed: false);
            var dlg = new MessageDialog(title, message, DialogKind.Error);
            dlg.ShowDialog();
            TracePopup("CLOSED", "Error", title, message, suppressed: false);
        }

        /// <summary>
        /// G-Code stop / emergency sequence boyunca ilerleme göstermek için kalıcı pencere.
        /// Idle olunca (autoCloseOnIdle=true) otomatik kapanır.
        /// </summary>
        public static MessageDialog ShowPersistentStopDialog(string title, string message, bool autoCloseOnIdle = true)
        {
            TracePopup("SHOW", "PersistentStop", title, message, suppressed: false);
            var dlg = new MessageDialog(title, message, DialogKind.PersistentStop, autoCloseOnIdle);
            dlg.Show(); // modal değil
            return dlg;
        }

        /// <summary>
        /// İlerleme satırı ekle (PersistentStop için)
        /// </summary>
        public void AppendProgress(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || ProgressLogText == null) return;
            Dispatcher?.Invoke(() =>
            {
                _logBuilder.AppendLine(line);
                ProgressLogText.Text = _logBuilder.ToString();
            });
        }

        private void StartIdleWatcher()
        {
            _idleWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _idleWatchTimer.Tick += (s, e) =>
            {
                try
                {
                    if (!_autoCloseOnIdle) return;
                    var status = CncControlApp.App.MainController?.MachineStatus ?? string.Empty;
                    if (status.StartsWith("Idle", StringComparison.OrdinalIgnoreCase))
                    {
                        AppendProgress("> ✅ Machine returned to IDLE – closing dialog");
                        _idleWatchTimer.Stop();
                        _idleWatchTimer = null;
                        Close();
                    }
                }
                catch { }
            };
            _idleWatchTimer.Start();
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            _result = true;
            DialogResult = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            _result = false;
            DialogResult = true; // DialogResult set (false sonucu _result üzerinden okunur)
            Close();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            _result = true;
            DialogResult = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _idleWatchTimer?.Stop(); } catch { }
            _idleWatchTimer = null;
            base.OnClosed(e);
        }
    }
}