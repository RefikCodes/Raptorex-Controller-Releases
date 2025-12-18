using System;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace CncControlApp.Controls
{
    public partial class ProbeLivePopup : Window
    {
        private readonly StringBuilder _sb = new StringBuilder(2048);
        private DispatcherTimer _timer;
        private Func<double> _zSource;
        private double _baselinePrevAvg;
        private double _threshold;
        private Action _onThreshold;
        private bool _thresholdFired;
        private int _consecutiveRequired = 2;
        private int _consec;
        
        /// <summary>
        /// Cancel butonuna basıldığında tetiklenir
        /// </summary>
        public event EventHandler Cancelled;
        
        /// <summary>
        /// Sekans iptal edildi mi?
        /// </summary>
        public bool IsCancelled { get; private set; }

        public ProbeLivePopup()
        {
            InitializeComponent();
        }

        public void StartLive(double prevAvg, double threshold, Func<double> zSource, int intervalMs = 200, Action onThreshold = null, int consecutiveRequired = 2)
        {
            _baselinePrevAvg = prevAvg;
            _threshold = threshold;
            _zSource = zSource;
            _onThreshold = onThreshold;
            _consecutiveRequired = Math.Max(1, consecutiveRequired);
            _consec = 0;
            _thresholdFired = false;
            if (_timer == null)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
                _timer.Tick += (s, e) =>
                {
                    try
                    {
                        double z = _zSource != null ? _zSource() : double.NaN;
                        if (!double.IsNaN(z) && !double.IsInfinity(z))
                        {
                            double deltaDown = _baselinePrevAvg - z;
                            CoarseLine.Text = $"deltaDown = prevAvg − Z = {_baselinePrevAvg:F3} − {z:F3} = {deltaDown:0.000} | kriter: ≥ {_threshold:0.000}";
                            if (!_thresholdFired)
                            {
                                if (deltaDown >= _threshold)
                                {
                                    _consec++;
                                    if (_consec >= _consecutiveRequired)
                                    {
                                        _thresholdFired = true;
                                        try { _onThreshold?.Invoke(); } catch { }
                                    }
                                }
                                else
                                {
                                    _consec = 0; // reset streak
                                }
                            }
                        }
                    }
                    catch { }
                };
            }
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, intervalMs));
            _timer.Start();
        }

        public void StopLive()
        {
            try { _timer?.Stop(); } catch { }
        }

        public void UpdateCoarse(double prevAvg, double coarseZ, double deltaDown, double threshold)
        {
            SafeUI(() =>
            {
                CoarseLine.Text = $"deltaDown = prevAvg − coarseZ = {prevAvg:F3} − {coarseZ:F3} = {deltaDown:0.000} | kriter: ≥ {threshold:0.000}";
                _baselinePrevAvg = prevAvg; // keep baseline synced if changed
                _threshold = threshold;
                _consec = 0; // reset consecutive counter on explicit update
                _thresholdFired = false;
            });
        }

        public void AppendLog(string text)
        {
            SafeUI(() =>
            {
                _sb.AppendLine(text);
                LogText.Text = _sb.ToString();
                // ScrollViewer is wrapping LogText; use its name to scroll
                var sv = this.FindName("LogScroll") as System.Windows.Controls.ScrollViewer;
                sv?.ScrollToEnd();
            });
        }

        private void SafeUI(Action a)
        {
            if (Dispatcher.CheckAccess()) a();
            else Dispatcher.Invoke(a);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { Close(); } catch { }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsCancelled = true;
            try { _timer?.Stop(); } catch { }
            try { Cancelled?.Invoke(this, EventArgs.Empty); } catch { }
            try { Close(); } catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _timer?.Stop(); } catch { }
            base.OnClosed(e);
        }
    }
}
