using System;
using System.Windows;
using System.Windows.Threading;
using CncControlApp.Controls;

namespace CncControlApp
{
    public partial class GCodeView
    {
        private void GCodeTabButton_Click(object sender, RoutedEventArgs e) => SetActiveTab(true);

        private void ViewTabButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // ✅ OPTIMIZED: Check if already on View tab to avoid redundant redraw
                if (ViewTabButton?.Tag as string == "Active")
                {
                    System.Diagnostics.Debug.WriteLine("Already on View tab - skipping redraw");
                    return;
                }

                SetActiveTab(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ViewTabButton_Click error: {ex.Message}");
            }
        }

        private void FeedRpmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var popup = new FeedRpmPopup(this)
                {
                    Owner = Application.Current?.MainWindow
                };
                popup.Show();
            }
            catch { }
        }

        private void GCodeRotationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var popup = new RotationPopup(this)
                {
                    Owner = Application.Current?.MainWindow
                };
                popup.ShowDialog();
            }
            catch { }
        }

        private void SetActiveTab(bool gcode)
        {
            try
            {
                if (GCodeTabButton != null) GCodeTabButton.Tag = gcode ? "Active" : null;
                if (ViewTabButton != null) ViewTabButton.Tag = gcode ? null : "Active";

                if (GCodeTabContent != null)
                    GCodeTabContent.Visibility = gcode ? Visibility.Visible : Visibility.Collapsed;
                if (ViewTabContent != null)
                    ViewTabContent.Visibility = gcode ? Visibility.Collapsed : Visibility.Visible;

                if (!gcode)
                {
                    RefreshMultiView();
                }
            }
            catch { }
        }

        private void RefreshMultiView()
        {
            try
            {
                if (_viewportManager == null || _fileService == null)
                {
                    System.Diagnostics.Debug.WriteLine("RefreshMultiView skipped: Missing manager or service");
                    return;
                }

                // ✅ OPTIMIZED: Use cached viewport check
                bool hasLoadedFile = _fileService.IsFileLoaded == true;

                if (hasLoadedFile)
                {
                    // ✅ OPTIMIZED: Use immediate rendering instead of BeginInvoke for better responsiveness
                    if (TopViewCanvas != null && TopViewCanvas.ActualWidth > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"RefreshMultiView: Immediate redraw, canvas size: {TopViewCanvas.ActualWidth:F0}x{TopViewCanvas.ActualHeight:F0}");

                        // ✅ IMMEDIATE: Direct call without dispatcher for faster response
                        _fileService.RedrawAllViewports();
                        EnsureOverlaysAfterRedraw();
                    }
                    else
                    {
                        // ✅ FALLBACK: Only use BeginInvoke if canvas not ready
                        System.Diagnostics.Debug.WriteLine("RefreshMultiView: Deferred redraw (canvas not ready)");
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                _fileService.RedrawAllViewports();
                                EnsureOverlaysAfterRedraw();
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Deferred redraw error: {ex.Message}");
                            }
                        }), DispatcherPriority.Loaded);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("RefreshMultiView: Reset viewports (no file loaded)");
                    _viewportManager.ResetAllViewports();
                    _visualization?.RefreshAllStaticOverlays();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshMultiView error: {ex.Message}");
            }
        }

        private void EnsureOverlaysAfterRedraw()
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _visualization?.RefreshAllStaticOverlays();
                }), DispatcherPriority.Background);
            }
            catch { }
        }

        private void TopViewHost_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _overlayManager?.RefreshOverlay();
            }
            catch { }
        }

        private void TopViewHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                if (_currentRotationAngle != 0)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ApplySimpleRotation(_currentRotationAngle);
                        _overlayManager?.OnCanvasSizeChanged();
                    }), DispatcherPriority.Background);
                }
                else
                {
                    _overlayManager?.OnCanvasSizeChanged();
                }
            }
            catch { }
        }
    }
}