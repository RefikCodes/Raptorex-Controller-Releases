using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace CncControlApp.Controls
{
    public partial class LineInputDialog : Window
    {
        public int? LineNumber { get; private set; }
        
        public LineInputDialog(int defaultLine = 1)
        {
            InitializeComponent();
            LineNumberTextBox.Text = defaultLine.ToString();
            LineNumberTextBox.Focus();
            LineNumberTextBox.SelectAll();
        }
        
        private void LineNumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Sadece rakam kabul et
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }
        
        private void LineNumberTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AcceptInput();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }
        
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            AcceptInput();
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void AcceptInput()
        {
            if (int.TryParse(LineNumberTextBox.Text, out int lineNum) && lineNum >= 1)
            {
                LineNumber = lineNum;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Lütfen geçerli bir satır numarası girin (1 veya daha büyük).", 
                    "Geçersiz Giriş", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
