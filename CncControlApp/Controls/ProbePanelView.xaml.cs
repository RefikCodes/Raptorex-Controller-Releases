using System;
using System.Windows;
using System.Windows.Controls;

namespace CncControlApp.Controls
{
    public partial class ProbePanelView : UserControl
    {
        // Probe events
        public event RoutedEventHandler ZProbeClicked;
        public event RoutedEventHandler PlusXProbeClicked;
        public event RoutedEventHandler MinusXProbeClicked;
        public event RoutedEventHandler PlusYProbeClicked;
        public event RoutedEventHandler MinusYProbeClicked;

        // Center from outer edges events
        public event RoutedEventHandler CenterXOuterClicked;
        public event RoutedEventHandler CenterYOuterClicked;
        public event RoutedEventHandler CenterXYOuterClicked;

        // Center from inner edges events
        public event RoutedEventHandler CenterXInnerClicked;
        public event RoutedEventHandler CenterYInnerClicked;
        public event RoutedEventHandler CenterXYInnerClicked;

        public ProbePanelView()
        {
            InitializeComponent();
            this.DataContext = App.MainController;
        }

        // Probe button event handlers
        private void ZProbeButton_Click(object sender, RoutedEventArgs e) => ZProbeClicked?.Invoke(sender, e);
        private void PlusXProbeButton_Click(object sender, RoutedEventArgs e) => PlusXProbeClicked?.Invoke(sender, e);
        private void MinusXProbeButton_Click(object sender, RoutedEventArgs e) => MinusXProbeClicked?.Invoke(sender, e);
        private void PlusYProbeButton_Click(object sender, RoutedEventArgs e) => PlusYProbeClicked?.Invoke(sender, e);
        private void MinusYProbeButton_Click(object sender, RoutedEventArgs e) => MinusYProbeClicked?.Invoke(sender, e);

        // Center from outer edges event handlers
        private void CenterXOuter_Click(object sender, RoutedEventArgs e) => CenterXOuterClicked?.Invoke(sender, e);
        private void CenterYOuter_Click(object sender, RoutedEventArgs e) => CenterYOuterClicked?.Invoke(sender, e);
        private void CenterXYOuter_Click(object sender, RoutedEventArgs e) => CenterXYOuterClicked?.Invoke(sender, e);

        // Center from inner edges event handlers
        private void CenterXInner_Click(object sender, RoutedEventArgs e) => CenterXInnerClicked?.Invoke(sender, e);
        private void CenterYInner_Click(object sender, RoutedEventArgs e) => CenterYInnerClicked?.Invoke(sender, e);
        private void CenterXYInner_Click(object sender, RoutedEventArgs e) => CenterXYInnerClicked?.Invoke(sender, e);
    }
}
