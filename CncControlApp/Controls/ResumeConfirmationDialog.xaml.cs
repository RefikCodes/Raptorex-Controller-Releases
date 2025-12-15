using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using CncControlApp.Services;

namespace CncControlApp.Controls
{
    public partial class ResumeConfirmationDialog : Window
    {
        public bool Confirmed { get; private set; } = false;
        public bool RunPreamble => chkRunPreamble.IsChecked == true;
        
        public int ResumeFromLine { get; private set; }
        public GCodeResumeService.GCodeModalState ModalState { get; private set; }
        public List<string> PreambleLines { get; private set; }
        public List<string> ResumeCommands { get; private set; }
        
        /// <summary>
        /// Event: Git butonuna basıldığında tetiklenir
        /// </summary>
        public event System.EventHandler<GoToPositionEventArgs> GoToPositionRequested;
        
        public ResumeConfirmationDialog()
        {
            InitializeComponent();
        }
        
        /// <summary>
        /// Dialog'u verilerle doldur
        /// </summary>
        public void SetResumeData(
            int stoppedLine, 
            int totalLines,
            GCodeResumeService.GCodeModalState modalState,
            List<string> preambleLines,
            List<string> resumeCommands)
        {
            ResumeFromLine = stoppedLine;
            ModalState = modalState;
            PreambleLines = preambleLines;
            ResumeCommands = resumeCommands;
            
            // UI'ı güncelle
            txtStoppedLine.Text = $"{stoppedLine + 1}";
            txtTotalLines.Text = $"{totalLines}";
            
            // ⚠️ SON POZİSYON - Makine buraya getirilmeli!
            txtLastX.Text = modalState.LastX.ToString("F3", CultureInfo.InvariantCulture) + " mm";
            txtLastY.Text = modalState.LastY.ToString("F3", CultureInfo.InvariantCulture) + " mm";
            txtLastZ.Text = modalState.LastZ.ToString("F3", CultureInfo.InvariantCulture) + " mm";
            
            // Modal state
            txtCoordSystem.Text = modalState.CoordinateSystem;
            txtDistanceMode.Text = $"{modalState.DistanceMode} ({(modalState.DistanceMode == "G90" ? "Absolute" : "Incremental")})";
            txtUnits.Text = $"{modalState.Units} ({(modalState.Units == "G21" ? "mm" : "inch")})";
            txtSpindle.Text = $"{modalState.SpindleState} @ S{modalState.SpindleSpeed}";
            txtFeedRate.Text = $"F{modalState.FeedRate}";
            txtCoolant.Text = modalState.CoolantState;
            txtTool.Text = $"T{modalState.ToolNumber}";
            
            // Preamble info
            txtPreambleInfo.Text = preambleLines.Count > 0 
                ? $"{preambleLines.Count} satır hazırlık kodu bulundu" 
                : "Hazırlık kodu bulunamadı";
            
            // Resume commands
            txtResumeCommands.Text = string.Join("\n", resumeCommands);
        }
        
        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }
        
        private void GoToPosition_Click(object sender, RoutedEventArgs e)
        {
            if (ModalState != null)
            {
                // Event ile MainController'a bildir - o G0 komutu gönderecek
                GoToPositionRequested?.Invoke(this, new GoToPositionEventArgs(
                    ModalState.LastX, 
                    ModalState.LastY, 
                    ModalState.LastZ));
            }
        }
    }
    
    /// <summary>
    /// Git butonu event argümanları
    /// </summary>
    public class GoToPositionEventArgs : System.EventArgs
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        
        public GoToPositionEventArgs(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
