using System;
using System.Windows;
using System.Windows.Controls;

namespace CncControlApp.Controls
{
    public partial class ProbePanelView : UserControl
    {
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

        public ProbePanelView()
        {
            InitializeComponent();
            this.DataContext = App.MainController; // inherit controller
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

        // Expose named elements for host to access if needed
        public Canvas GridLinesCanvas => MainGridLinesCanvas;
        public Canvas ProbeCoordinatesCanvas => MainProbeCoordinatesCanvas;
        public Canvas CrosshairCanvas => MainCrosshairCanvas;
        public TextBlock ProbeXText => MainProbeXCoordinate;
        public TextBlock ProbeYText => MainProbeYCoordinate;
        public TextBlock ProbeZText => MainProbeZCoordinate;
    }
}
