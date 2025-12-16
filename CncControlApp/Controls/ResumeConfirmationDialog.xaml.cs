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
        
        public ResumeConfirmationDialog()
        {
            InitializeComponent();
            
            // Z onay checkbox değişikliğini dinle
            chkZeroConfirmed.Checked += (s, e) => UpdateConfirmButtonState();
            chkZeroConfirmed.Unchecked += (s, e) => UpdateConfirmButtonState();
        }
        
        private void UpdateConfirmButtonState()
        {
            btnConfirm.IsEnabled = chkZeroConfirmed.IsChecked == true;
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
            
            // Başlangıç koordinatları (bilgi amaçlı)
            txtLastX.Text = modalState.LastX.ToString("F3", CultureInfo.InvariantCulture) + " mm";
            txtLastY.Text = modalState.LastY.ToString("F3", CultureInfo.InvariantCulture) + " mm";
            txtLastZ.Text = modalState.LastZ.ToString("F3", CultureInfo.InvariantCulture) + " mm";
            
            // Modal state özeti (kompakt)
            var summaryParts = new List<string>();
            summaryParts.Add(modalState.CoordinateSystem);
            summaryParts.Add(modalState.DistanceMode);
            summaryParts.Add(modalState.Units);
            if (!string.IsNullOrEmpty(modalState.SpindleState) && modalState.SpindleState != "M5")
                summaryParts.Add($"{modalState.SpindleState} S{modalState.SpindleSpeed}");
            if (modalState.FeedRate > 0)
                summaryParts.Add($"F{modalState.FeedRate}");
            if (!string.IsNullOrEmpty(modalState.CoolantState) && modalState.CoolantState != "M9")
                summaryParts.Add(modalState.CoolantState);
            
            txtModalSummary.Text = string.Join(" | ", summaryParts);
            
            // Preamble info
            txtPreambleInfo.Text = preambleLines.Count > 0 
                ? $"{preambleLines.Count} satır hazırlık kodu bulundu" 
                : "Hazırlık kodu bulunamadı";
            
            // Devam Et butonu başlangıçta devre dışı (Z onayı bekliyor)
            UpdateConfirmButtonState();
        }
        
        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (chkZeroConfirmed.IsChecked != true)
            {
                MessageDialog.ShowError("Onay Gerekli", "Devam etmeden önce Z sıfırının doğru olduğunu onaylamalısınız!");
                return;
            }
            
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
    }
    
    /// <summary>
    /// Git butonu event argümanları (uyumluluk için kalıyor)
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
