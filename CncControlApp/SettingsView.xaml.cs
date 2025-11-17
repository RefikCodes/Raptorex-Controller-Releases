using System.Windows.Controls;

namespace CncControlApp
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        
// Set DataContext to MainController so bindings work
            try
     {
     this.DataContext = App.MainController;
          }
catch { }
    }
    }
}