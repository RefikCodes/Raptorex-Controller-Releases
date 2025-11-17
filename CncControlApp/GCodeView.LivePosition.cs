using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace CncControlApp
{
    /// <summary>
    /// Live position tracking on TopView canvas during G-code execution
    /// </summary>
    public partial class GCodeView
    {
        private Ellipse _livePositionIndicator;
        private bool _isLivePositionEnabled = false;

        /// <summary>
        /// Initialize live position tracking
        /// </summary>
        private void InitializeLivePositionTracking()
        {
            try
            {
                // Subscribe to MachineStatus property changes
                if (App.MainController?.MStatus != null)
                {
                    App.MainController.MStatus.PropertyChanged -= MachineStatus_PropertyChanged;
                    App.MainController.MStatus.PropertyChanged += MachineStatus_PropertyChanged;
                }

                System.Diagnostics.Debug.WriteLine("✅ Live position tracking initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ InitializeLivePositionTracking error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle machine status coordinate changes
        /// </summary>
        private void MachineStatus_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                // Only update position if live tracking is enabled (during execution)
                if (!_isLivePositionEnabled) return;

                // Update on position changes (prefer machine coordinates to match drawing origin placement)
                if (e.PropertyName == nameof(MachineStatus.X) || e.PropertyName == nameof(MachineStatus.Y) ||
                    e.PropertyName == nameof(MachineStatus.WorkX) || e.PropertyName == nameof(MachineStatus.WorkY))
                {
                    UpdateLivePositionIndicator();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ MachineStatus_PropertyChanged error: {ex.Message}");
            }
        }

        /// <summary>
        /// Enable live position tracking (called when execution starts)
        /// </summary>
        public void EnableLivePositionTracking()
        {
            try
            {
                _isLivePositionEnabled = true;
                CreateLivePositionIndicator();
                UpdateLivePositionIndicator();
                System.Diagnostics.Debug.WriteLine("✅ Live position tracking ENABLED");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ EnableLivePositionTracking error: {ex.Message}");
            }
        }

        /// <summary>
        /// Disable live position tracking (called when execution stops)
        /// </summary>
        public void DisableLivePositionTracking()
        {
            try
            {
                _isLivePositionEnabled = false;
                RemoveLivePositionIndicator();
                System.Diagnostics.Debug.WriteLine("✅ Live position tracking DISABLED");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DisableLivePositionTracking error: {ex.Message}");
            }
        }

        /// <summary>
        /// Create the live position indicator visual element
        /// </summary>
        private void CreateLivePositionIndicator()
        {
            try
            {
                if (TopViewOverlayCanvas == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ TopViewOverlayCanvas is null");
                    return;
                }

                // Remove existing indicator if present
                RemoveLivePositionIndicator();

                // Create bright green indicator circle - larger and more visible
                _livePositionIndicator = new Ellipse
                {
                    Width = 20,
                    Height = 20,
                    Fill = new SolidColorBrush(Color.FromRgb(0, 255, 0)), // Bright green
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 255, 0)), // Yellow border for contrast
                    StrokeThickness = 3,
                    Opacity = 1.0
                };

                // Ensure it's on top
                Panel.SetZIndex(_livePositionIndicator, 9999);

                // Add to overlay canvas
                TopViewOverlayCanvas.Children.Add(_livePositionIndicator);

                System.Diagnostics.Debug.WriteLine($"✅ Live position indicator created on canvas with {TopViewOverlayCanvas.Children.Count} children");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CreateLivePositionIndicator error: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove the live position indicator from canvas
        /// </summary>
        private void RemoveLivePositionIndicator()
        {
            try
            {
                if (_livePositionIndicator != null && TopViewOverlayCanvas != null)
                {
                    TopViewOverlayCanvas.Children.Remove(_livePositionIndicator);
                    _livePositionIndicator = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ RemoveLivePositionIndicator error: {ex.Message}");
            }
        }

        /// <summary>
        /// Update live position indicator position based on current Work coordinates
        /// </summary>
        private void UpdateLivePositionIndicator()
        {
            try
            {
                if (_livePositionIndicator == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ Live indicator is null");
                    return;
                }
                
                if (TopViewOverlayCanvas == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ TopViewOverlayCanvas is null");
                    return;
                }
                
                if (App.MainController?.MStatus == null)
                {
                    System.Diagnostics.Debug.WriteLine("❌ MStatus is null");
                    return;
                }

                // Use MACHINE coordinates to match DrawGCodeCentered origin placement
                // (G-code origin is drawn at current machine position)
                double machineX = App.MainController.MStatus.X;
                double machineY = App.MainController.MStatus.Y;

                // Get canvas dimensions
                double canvasWidth = TopViewOverlayCanvas.ActualWidth;
                double canvasHeight = TopViewOverlayCanvas.ActualHeight;

                if (canvasWidth <= 0 || canvasHeight <= 0)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Invalid canvas size: {canvasWidth}x{canvasHeight}");
                    return;
                }

                // Build unified transform for THIS canvas using $130/$131
                if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                {
                    System.Diagnostics.Debug.WriteLine("❌ WorkspaceTransform not ready");
                    return;
                }

                var canvasPt = xf.ToCanvas(machineX, machineY);
                double canvasX = canvasPt.X;
                double canvasY = canvasPt.Y;

                // Position indicator (center it on the point)
                Canvas.SetLeft(_livePositionIndicator, canvasX - (_livePositionIndicator.Width / 2));
                Canvas.SetTop(_livePositionIndicator, canvasY - (_livePositionIndicator.Height / 2));

                // Debug first few updates and then occasionally
                if (_livePositionUpdateCount < 5 || _livePositionUpdateCount % 50 == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"🎯 Live position #{_livePositionUpdateCount}: Machine({machineX:F2},{machineY:F2}) → Canvas({canvasX:F1},{canvasY:F1}) [Scale:{xf.Scale:F3}, Table:{xf.MaxX:F1}x{xf.MaxY:F1}]");
                }
                _livePositionUpdateCount++;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ UpdateLivePositionIndicator error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private int _livePositionUpdateCount = 0;
    }
}
