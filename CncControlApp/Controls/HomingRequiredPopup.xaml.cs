using System;
using System.Windows;
using System.Windows.Input;

namespace CncControlApp
{
    public partial class HomingRequiredPopup : Window
    {
        public HomingRequiredPopup()
        {
            InitializeComponent();
            this.PreviewKeyDown += HomingRequiredPopup_PreviewKeyDown;
            this.Deactivated += (s,e)=> this.Activate();
        }

        private void HomingRequiredPopup_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Enter)
            {
                e.Handled = true; // Homing yapılmadan kapanmasını engelle
            }
        }

        private async void HomeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.MainController?.IsConnected == true)
                {
                    HomeButton.IsEnabled = false;
                    HomeButton.Content = "HOMING...";

                    bool homed = await App.MainController.HomeAllAsync();
                    if (homed)
                    {
                        // Doğrudan controller'a işaretle ve popup'ı kapat
                        App.MainController.MarkHomed();
                        HomeButton.Content = "COMPLETED";
                        // Kısa gecikme (status Idle güncellemesi için çok gerekmez ama görsel olarak rahat)
                        await System.Threading.Tasks.Task.Delay(300);
                        Close();
                    }
                    else
                    {
                        HomeButton.Content = "RETRY";
                        HomeButton.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> Homing popup hata: {ex.Message}");
                HomeButton.Content = "ERROR";
                HomeButton.IsEnabled = true;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!(App.MainController?.HasHomed ?? false))
            {
                e.Cancel = true;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Ana pencere yeniden etkin olsun
            if (Owner != null) Owner.IsEnabled = true;
            base.OnClosed(e);
        }
    }
}
