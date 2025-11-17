using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CncControlApp
{
    public partial class GCodeView
    {
        private void ScrollGCodeToTop()
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    EnsureGCodeScrollViewer();
                    if (_gcodeScrollViewer != null)
                    {
                        CancelSmoothScroll();
                        _gcodeScrollViewer.ScrollToVerticalOffset(0);
                    }
                    else
                    {
                        // Fallback: ensure first item visible
                        if (GCodeListBox != null && GCodeListBox.Items.Count > 0)
                        {
                            GCodeListBox.ScrollIntoView(GCodeListBox.Items[0]);
                            GCodeListBox.SelectedIndex = 0;
                        }
                    }
                }), DispatcherPriority.Background);
            }
            catch { }
        }
        private void EnsureGCodeScrollViewer()
        {
            try
            {
                if (_gcodeScrollViewer == null)
                {
                    _gcodeScrollViewer = GetScrollViewer(GCodeListBox);
                    if (_gcodeScrollViewer != null)
                    {
                        _gcodeScrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
                        _gcodeScrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
                    }
                }

                if (GCodeListBox != null)
                {
                    GCodeListBox.PreviewMouseWheel -= OnListBoxPreviewMouseWheel;
                    GCodeListBox.PreviewMouseWheel += OnListBoxPreviewMouseWheel;

                    GCodeListBox.PreviewMouseDown -= OnListBoxPreviewMouseDown;
                    GCodeListBox.PreviewMouseDown += OnListBoxPreviewMouseDown;
                }
            }
            catch { }
        }

        private void OnListBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _userScrollSuppressUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(1200);
            CancelSmoothScroll();
        }

        private void OnListBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _userScrollSuppressUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(800);
            CancelSmoothScroll();
        }

        private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isScrollAnimating) return;

            bool userLikely =
                (_gcodeScrollViewer?.IsMouseOver == true) ||
                Mouse.LeftButton == MouseButtonState.Pressed ||
                Mouse.MiddleButton == MouseButtonState.Pressed ||
                Mouse.RightButton == MouseButtonState.Pressed;

            if (userLikely && e.VerticalChange != 0)
            {
                _userScrollSuppressUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(800);
            }
        }

        private void BeginSmoothScrollTo(double targetTop)
        {
            if (_gcodeScrollViewer == null) return;

            double current = _gcodeScrollViewer.VerticalOffset;
            double delta = Math.Abs(targetTop - current);

            if (delta < 0.25)
            {
                _isScrollAnimating = true;
                _gcodeScrollViewer.ScrollToVerticalOffset(targetTop);
                CancelSmoothScroll();
                return;
            }

            _scrollFromOffset = current;
            _scrollToOffset = targetTop;
            _scrollStartTime = DateTime.UtcNow;

            if (_smoothScrollTimer == null)
            {
                _smoothScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                _smoothScrollTimer.Tick += (s, e) =>
                {
                    try
                    {
                        if (_gcodeScrollViewer == null)
                        {
                            CancelSmoothScroll();
                            return;
                        }

                        var elapsed = DateTime.UtcNow - _scrollStartTime;
                        double t = Math.Min(1.0, elapsed.TotalMilliseconds / _scrollDuration.TotalMilliseconds);
                        double eased = 1 - Math.Pow(1 - t, 3);
                        double val = _scrollFromOffset + (_scrollToOffset - _scrollFromOffset) * eased;

                        _gcodeScrollViewer.ScrollToVerticalOffset(val);

                        if (t >= 1.0)
                        {
                            CancelSmoothScroll();
                        }
                    }
                    catch
                    {
                        CancelSmoothScroll();
                    }
                };
            }

            _isScrollAnimating = true;
            if (!_smoothScrollTimer.IsEnabled)
                _smoothScrollTimer.Start();
        }

        private void CancelSmoothScroll()
        {
            if (_smoothScrollTimer != null)
                _smoothScrollTimer.Stop();
            _isScrollAnimating = false;
        }

        private void ScrollToCurrentLine()
        {
            try
            {
                if (GCodeListBox == null || DisplayGCodeLines == null || App.MainController == null) return;
                var manager = App.MainController.GCodeManager;
                if (manager == null) return;

                int currentIndex = manager.CurrentlyExecutingLineIndex;
                if (currentIndex < 0 || currentIndex >= DisplayGCodeLines.Count) return;

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    EnsureGCodeScrollViewer();

                    if (DateTime.UtcNow < _userScrollSuppressUntil)
                        return;

                    var sv = _gcodeScrollViewer;
                    var currentItem = DisplayGCodeLines[currentIndex];

                    if (sv == null)
                    {
                        GCodeListBox.ScrollIntoView(currentItem);
                        GCodeListBox.SelectedItem = currentItem;
                        return;
                    }

                    const int reserve = 5;
                    bool itemsMode = ScrollViewer.GetCanContentScroll(GCodeListBox) == true;
                    double viewport = sv.ViewportHeight;
                    double offset = sv.VerticalOffset;

                    if (itemsMode && viewport > 0)
                    {
                        int topIndex = (int)Math.Floor(offset);
                        int visibleCount = Math.Max(0, (int)Math.Floor(viewport));

                        if (visibleCount <= 0)
                        {
                            GCodeListBox.ScrollIntoView(currentItem);
                            GCodeListBox.SelectedItem = currentItem;
                            return;
                        }

                        int bottomIndex = topIndex + visibleCount - 1;
                        int bottomSpace = bottomIndex - currentIndex;

                        int desiredTop = topIndex;

                        if (currentIndex < topIndex || bottomSpace < reserve)
                        {
                            desiredTop = currentIndex - (visibleCount - 1 - reserve);
                            if (desiredTop < 0) desiredTop = 0;

                            int maxTop = Math.Max(0, DisplayGCodeLines.Count - visibleCount);
                            if (desiredTop > maxTop) desiredTop = maxTop;
                        }

                        if (Math.Abs(desiredTop - offset) >= 0.5)
                        {
                            if (Math.Abs(_lastRequestedTop - desiredTop) >= 0.5)
                            {
                                _lastRequestedTop = desiredTop;
                                BeginSmoothScrollTo(desiredTop);
                            }
                        }

                        GCodeListBox.SelectedItem = currentItem;
                    }
                    else
                    {
                        GCodeListBox.ScrollIntoView(currentItem);
                        GCodeListBox.SelectedItem = currentItem;
                    }
                }), DispatcherPriority.Background);
            }
            catch { }
        }

        private ScrollViewer GetScrollViewer(DependencyObject element)
        {
            try
            {
                if (element is ScrollViewer sv) return sv;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                {
                    var child = VisualTreeHelper.GetChild(element, i);
                    var result = GetScrollViewer(child);
                    if (result != null) return result;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private void OnGCodeListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_suppressSelectionChanged) return;

                if (App.MainController?.IsGCodeRunning == true || App.MainController?.CanContinueExecution == true)
                {
                    EnsureSelectedCurrentLine();
                }
            }
            catch { }
        }

        private void EnsureSelectedCurrentLine()
        {
            if (GCodeListBox == null || DisplayGCodeLines == null || App.MainController == null) return;
            var manager = App.MainController.GCodeManager;
            int currentIndex = manager?.CurrentlyExecutingLineIndex ?? -1;
            if (currentIndex < 0 || currentIndex >= DisplayGCodeLines.Count) return;

            if (GCodeListBox.SelectedIndex != currentIndex)
            {
                _suppressSelectionChanged = true;
                try { GCodeListBox.SelectedIndex = currentIndex; }
                finally { _suppressSelectionChanged = false; }
            }
        }

        private void ResetAllLineStatus()
        {
            try
            {
                if (DisplayGCodeLines == null) return;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (var item in DisplayGCodeLines) item.ResetStatus();
                }), DispatcherPriority.Background);
            }
            catch { }
        }
    }
}