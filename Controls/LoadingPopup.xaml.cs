using System;
using System.Windows;
using System.Windows.Threading;

namespace CncControlApp.Controls
{
    /// <summary>
    /// Interaction logic for LoadingPopup.xaml
    /// </summary>
    public partial class LoadingPopup : Window
    {
        private readonly DispatcherTimer _timer;
        private DateTime _startTime;

        public LoadingPopup()
        {
            InitializeComponent();
            
            _startTime = DateTime.Now;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Handle image loading fallback - resimdeki gibi RX text göster
            PngLogo.ImageFailed += (s, e) => 
            {
                PngLogo.Visibility = Visibility.Collapsed;
                FallbackLogo.Visibility = Visibility.Visible;
            };

            // Eğer logo yoksa fallback'i göster
            if (PngLogo.Source == null)
            {
                PngLogo.Visibility = Visibility.Collapsed;
                FallbackLogo.Visibility = Visibility.Visible;
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                var elapsed = DateTime.Now - _startTime;
                // Timer minimal kullanım için - gerekirse kaldırılabilir
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadingPopup timer error: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates status message - resimdeki tasarımda kullanılmıyor
        /// </summary>
        /// <param name="status">New status message</param>
        public void UpdateStatus(string status)
        {
            try
            {
                // Resimdeki tasarımda status text yok, compatibility için bırakıldı
                System.Diagnostics.Debug.WriteLine($"LoadingPopup Status: {status}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadingPopup UpdateStatus error: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates settings count - resimdeki tasarımda kullanılmıyor
        /// </summary>
        /// <param name="count">Number of loaded settings</param>
        public void UpdateSettingsCount(int count)
        {
            try
            {
                // Resimdeki tasarımda count gösterilmiyor, compatibility için bırakıldı
                System.Diagnostics.Debug.WriteLine($"LoadingPopup Settings Count: {count}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadingPopup UpdateSettingsCount error: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely closes the popup
        /// </summary>
        public void ClosePopup()
        {
       try
  {
      if (Application.Current?.Dispatcher != null)
             {
        // ✅ HEMEN KAPAT - GECİKME YOK
       Application.Current.Dispatcher.Invoke(new Action(() =>
      {
  _timer?.Stop();
     // ✅ FADE OUT YOK - DİREKT KAPAT
        this.Opacity = 0; // Hemen görünmez yap
  this.Close();
     }));
             }
     }
       catch (Exception ex)
      {
    System.Diagnostics.Debug.WriteLine($"LoadingPopup ClosePopup error: {ex.Message}");
   try { this.Close(); } catch { }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _timer?.Stop();
                base.OnClosed(e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadingPopup OnClosed error: {ex.Message}");
            }
        }
    }
}
