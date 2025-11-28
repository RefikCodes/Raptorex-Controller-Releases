using System;
using System.Windows;                 // needed for Application.Current
using System.Windows.Threading;

namespace CncControlApp
{
    public partial class GCodeView
    {
        // Throttle state
        private DispatcherTimer _rotationDebounceTimer;
        private double _pendingRotationAngle = 0;

        // Leading+trailing throttle control
        private DateTime _lastRotationUpdate = DateTime.MinValue;
        private DateTime _nextRotationAllowed = DateTime.MinValue;
        private bool _rotationTrailingScheduled = false;

        // ~1/4 of previous (target ~10 updates/sec)
        private readonly TimeSpan _rotationUpdateThrottle = TimeSpan.FromMilliseconds(100);

        // Change detection
        private double _lastReportedXRange = double.NaN;
        private double _lastReportedYRange = double.NaN;
        private double _lastAngleApplied = double.NaN;

        private void EnsureRotationDebounceTimer()
        {
            if (_rotationDebounceTimer != null) return;

            _rotationDebounceTimer = new DispatcherTimer();
            _rotationDebounceTimer.Tick += (s, e) =>
            {
                try
                {
                    _rotationDebounceTimer.Stop();
                    _rotationTrailingScheduled = false;

                    // trailing call with the latest angle
                    ApplyRotationLiveStats(_pendingRotationAngle);
                    _nextRotationAllowed = DateTime.UtcNow + _rotationUpdateThrottle;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Rotation throttle error: {ex.Message}");
                }
            };
        }

        // Call this from the slider handler (throttled updates during movement)
        public void UpdateXDistanceLiveFromRotationThrottled()
        {
            try
            {
                if (_fileService?.GCodeSegments == null || _fileService.GCodeSegments.Count == 0)
                    return;

                _pendingRotationAngle = _currentRotationAngle;

                EnsureRotationDebounceTimer();

                var now = DateTime.UtcNow;

                // Leading edge: if allowed, run immediately and open a new window
                if (now >= _nextRotationAllowed)
                {
                    _rotationDebounceTimer.Stop();
                    _rotationTrailingScheduled = false;

                    ApplyRotationLiveStats(_pendingRotationAngle);
                    _nextRotationAllowed = now + _rotationUpdateThrottle;
                    return;
                }

                // Otherwise, schedule a trailing call at the earliest allowed time (coalesced)
                if (!_rotationTrailingScheduled)
                {
                    var delay = _nextRotationAllowed - now;
                    if (delay < TimeSpan.FromMilliseconds(5))
                        delay = TimeSpan.FromMilliseconds(5);

                    _rotationDebounceTimer.Stop();
                    _rotationDebounceTimer.Interval = delay;
                    _rotationDebounceTimer.Start();
                    _rotationTrailingScheduled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateXDistanceLiveFromRotationThrottled error: {ex.Message}");
            }
        }

        private void ApplyRotationLiveStats(double angle)
        {
            try
            {
                if (_fileService?.GCodeSegments == null || _fileService.GCodeSegments.Count == 0)
                    return;

                // Minimal angle step + time-based throttle inside the worker as well (noise guard)
                const double angleEps = 0.25; // degrees
                var now = DateTime.UtcNow;

                if (!double.IsNaN(_lastAngleApplied) &&
                    Math.Abs(angle - _lastAngleApplied) < angleEps &&
                    (now - _lastRotationUpdate) < _rotationUpdateThrottle)
                {
                    return;
                }

                _lastRotationUpdate = now;
                _lastAngleApplied = angle;

                var b = CalculateRotatedBounds(_fileService.GCodeSegments, angle);
                double xRange = b.MaxX - b.MinX;
                double yRange = b.MaxY - b.MinY;

                const double eps = 0.1; // millimeters
                bool significantlyChanged =
                    double.IsNaN(_lastReportedXRange) || Math.Abs(xRange - _lastReportedXRange) >= eps ||
                    double.IsNaN(_lastReportedYRange) || Math.Abs(yRange - _lastReportedYRange) >= eps;

                if (!significantlyChanged) return;

                // Update cached dimensions for live fit check
                UpdateCurrentPartDimensions(xRange, yRange);

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (XDistanceTextBlock != null) XDistanceTextBlock.Text = $"{xRange:F1}mm";
                        if (YDistanceTextBlock != null) YDistanceTextBlock.Text = $"{yRange:F1}mm";

                        // Update status bar including live fit state
                        UpdateStatusBarWithLiveFitCheck();
                    }
                    catch (Exception exUi)
                    {
                        System.Diagnostics.Debug.WriteLine($"ApplyRotationLiveStats UI update error: {exUi.Message}");
                    }
                }), DispatcherPriority.Background);

                _lastReportedXRange = xRange;
                _lastReportedYRange = yRange;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ApplyRotationLiveStats error: {ex.Message}");
            }
        }
    }
}