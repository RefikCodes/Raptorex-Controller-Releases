using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CncControlApp.Controls
{
    /// <summary>
    /// Z Mapping grid parametreleri için event args
    /// </summary>
    public class ZMappingEventArgs : EventArgs
    {
        public int RowCount { get; }
        public int ColumnCount { get; }

        public ZMappingEventArgs(int rows, int columns)
        {
            RowCount = rows;
            ColumnCount = columns;
        }
    }

    /// <summary>
    /// Z Mapping nokta verisi modeli
    /// </summary>
    public class ZMappingPoint
    {
        public int PointNumber { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double ZDifference { get; set; }
        public bool IsProbed { get; set; }
    }

    public partial class ProbePanelView : UserControl
    {
        /// <summary>
        /// Z Mapping noktaları koleksiyonu
        /// </summary>
        public ObservableCollection<ZMappingPoint> ZMappingPoints { get; } = new ObservableCollection<ZMappingPoint>();
        /// <summary>
        /// Dependency property for GCode loaded state - controls Haritalama button enabled state
        /// </summary>
        public static readonly DependencyProperty IsGCodeLoadedProperty =
            DependencyProperty.Register("IsGCodeLoaded", typeof(bool), typeof(ProbePanelView),
                new PropertyMetadata(false, OnIsGCodeLoadedChanged));

        public bool IsGCodeLoaded
        {
            get => (bool)GetValue(IsGCodeLoadedProperty);
            set => SetValue(IsGCodeLoadedProperty, value);
        }

        private static void OnIsGCodeLoadedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProbePanelView panel)
            {
                panel.UpdateHaritalamaState();
            }
        }

        private void UpdateHaritalamaState()
        {
            if (FindCornerModeRadio != null)
            {
                FindCornerModeRadio.IsEnabled = IsGCodeLoaded;
                
                // GCode yüklü değilse ve haritalama seçiliyse, Probe moduna geç
                if (!IsGCodeLoaded && FindCornerModeRadio.IsChecked == true)
                {
                    ProbeModeRadio.IsChecked = true;
                }
            }
        }

        public event RoutedEventHandler ZProbeClicked;
        public event RoutedEventHandler PlusXProbeClicked;
        public event RoutedEventHandler MinusXProbeClicked;
        public event RoutedEventHandler PlusYProbeClicked;
        public event RoutedEventHandler MinusYProbeClicked;
        public event RoutedEventHandler SetZeroXClicked;
        public event RoutedEventHandler SetZeroYClicked;
        public event RoutedEventHandler SetZeroZClicked;
        public event RoutedEventHandler SetZeroAClicked;

        // New events for center/corner modes
        public event RoutedEventHandler CenterXOuterClicked;
        public event RoutedEventHandler CenterYOuterClicked;
        public event RoutedEventHandler CenterXYOuterClicked;

        // Z Mapping event
        public event EventHandler<ZMappingEventArgs> ZMappingRequested;

        public ProbePanelView()
        {
            InitializeComponent();
            this.DataContext = App.MainController; // inherit controller
            
            // Initial state - haritalama disabled until GCode loaded
            Loaded += (s, e) => UpdateHaritalamaState();
        }

        private void ZProbeButton_Click(object sender, RoutedEventArgs e) => ZProbeClicked?.Invoke(sender, e);
        private void PlusXProbeButton_Click(object sender, RoutedEventArgs e) => PlusXProbeClicked?.Invoke(sender, e);
        private void MinusXProbeButton_Click(object sender, RoutedEventArgs e) => MinusXProbeClicked?.Invoke(sender, e);
        private void PlusYProbeButton_Click(object sender, RoutedEventArgs e) => PlusYProbeClicked?.Invoke(sender, e);
        private void MinusYProbeButton_Click(object sender, RoutedEventArgs e) => MinusYProbeClicked?.Invoke(sender, e);
        private void SetZeroX_Click(object sender, RoutedEventArgs e) => SetZeroXClicked?.Invoke(sender, e);
        private void SetZeroY_Click(object sender, RoutedEventArgs e) => SetZeroYClicked?.Invoke(sender, e);
        private void SetZeroZ_Click(object sender, RoutedEventArgs e) => SetZeroZClicked?.Invoke(sender, e);
        private void SetZeroA_Click(object sender, RoutedEventArgs e) => SetZeroAClicked?.Invoke(sender, e);

        // Routed handlers for center-from-outer mode buttons
        private void CenterXOuter_Click(object sender, RoutedEventArgs e) => CenterXOuterClicked?.Invoke(sender, e);
        private void CenterYOuter_Click(object sender, RoutedEventArgs e) => CenterYOuterClicked?.Invoke(sender, e);
        private void CenterXYOuter_Click(object sender, RoutedEventArgs e) => CenterXYOuterClicked?.Invoke(sender, e);

        // Sadece sayı girişine izin ver
        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }

        // Z Mapping OK button handler - inline textbox'lardan değer alır
        private void ZMappingOk_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(RowCountTextBox?.Text, out int rows) &&
                int.TryParse(ColumnCountTextBox?.Text, out int cols))
            {
                if (rows < 2 || rows > 20)
                {
                    MessageDialog.ShowError("Geçersiz Değer", "Satır sayısı 2-20 arasında olmalıdır.");
                    RowCountTextBox?.Focus();
                    RowCountTextBox?.SelectAll();
                    return;
                }

                if (cols < 2 || cols > 20)
                {
                    MessageDialog.ShowError("Geçersiz Değer", "Sütun sayısı 2-20 arasında olmalıdır.");
                    ColumnCountTextBox?.Focus();
                    ColumnCountTextBox?.SelectAll();
                    return;
                }

                // Event'i tetikle - MainWindow grid'i çizecek ve bounds değerlerini gönderecek
                ZMappingRequested?.Invoke(this, new ZMappingEventArgs(rows, cols));
            }
            else
            {
                MessageDialog.ShowError("Geçersiz Giriş", "Lütfen geçerli sayılar girin.");
            }
        }

        /// <summary>
        /// Z Mapping noktalarını DataGrid'e yükler
        /// </summary>
        public void PopulateZMappingPoints(double minX, double maxX, double minY, double maxY, int rows, int columns)
        {
            ZMappingPoints.Clear();

            double width = maxX - minX;
            double height = maxY - minY;
            double cellWidth = width / (columns - 1);
            double cellHeight = height / (rows - 1);

            int pointNumber = 1;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    double x = minX + c * cellWidth;
                    double y = minY + r * cellHeight;

                    ZMappingPoints.Add(new ZMappingPoint
                    {
                        PointNumber = pointNumber++,
                        X = x,
                        Y = y,
                        Z = 0,
                        ZDifference = 0,
                        IsProbed = false
                    });
                }
            }

            // DataGrid'e bağla
            ZMappingDataGrid.ItemsSource = ZMappingPoints;
        }

        /// <summary>
        /// Belirli bir noktanın Z değerini günceller
        /// </summary>
        public void UpdateZMappingPointZ(int pointNumber, double zValue)
        {
            if (pointNumber < 1 || pointNumber > ZMappingPoints.Count) return;

            var point = ZMappingPoints[pointNumber - 1];
            point.Z = zValue;
            point.IsProbed = true;

            // İlk noktaya göre Z farkını hesapla
            if (ZMappingPoints.Count > 0 && ZMappingPoints[0].IsProbed)
            {
                double referenceZ = ZMappingPoints[0].Z;
                foreach (var p in ZMappingPoints)
                {
                    if (p.IsProbed)
                    {
                        p.ZDifference = p.Z - referenceZ;
                    }
                }
            }

            // DataGrid'i yenile
            ZMappingDataGrid.Items.Refresh();
        }

        // Canvas elements removed - no longer needed
        /*
        public Canvas GridLinesCanvas => MainGridLinesCanvas;
        public Canvas ProbeCoordinatesCanvas => MainProbeCoordinatesCanvas;
        public Canvas CrosshairCanvas => MainCrosshairCanvas;
        public TextBlock ProbeXText => MainProbeXCoordinate;
        public TextBlock ProbeYText => MainProbeYCoordinate;
        public TextBlock ProbeZText => MainProbeZCoordinate;
        */
    }
}
