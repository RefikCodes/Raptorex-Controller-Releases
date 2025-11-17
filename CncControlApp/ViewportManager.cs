using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CncControlApp
{
    /// <summary>
    /// Manages individual viewport zoom, pan, and transformations
    /// </summary>
    public class ViewportManager
    {
        #region Constants

        private const double MIN_ZOOM_FACTOR = 0.1;  // Minimum zoom out
        private const double MAX_ZOOM_FACTOR = 10.0; // Maximum zoom in

        #endregion

        #region Fields

        private Dictionary<ViewportType, double> _viewportZoomFactors;
        private Dictionary<ViewportType, Point> _viewportPanOffsets;
        private Dictionary<ViewportType, Canvas> _viewportCanvases;
        private Dictionary<ViewportType, bool> _viewportDragStates;
        private Dictionary<ViewportType, Point> _viewportLastMousePositions;
        private Dictionary<ViewportType, DateTime> _lastClickTimes;

        #endregion

        #region Constructor

        public ViewportManager(Canvas topCanvas, Canvas frontCanvas, Canvas rightCanvas, Canvas isometricCanvas)
        {
            InitializeViewportData(topCanvas, frontCanvas, rightCanvas, isometricCanvas);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Setup mouse events for each viewport
        /// </summary>
        public void SetupViewportMouseEvents()
        {
            try
            {
                foreach (var kvp in _viewportCanvases)
                {
                    var viewportType = kvp.Key;
                    var canvas = kvp.Value;

                    if (canvas != null)
                    {
                        // Mouse wheel for zoom
                        canvas.MouseWheel += (sender, e) => ViewportCanvas_MouseWheel(sender, e, viewportType);
                        
                        // Mouse events for pan
                        canvas.MouseLeftButtonDown += (sender, e) => ViewportCanvas_MouseLeftButtonDown(sender, e, viewportType);
                        canvas.MouseMove += (sender, e) => ViewportCanvas_MouseMove(sender, e, viewportType);
                        canvas.MouseLeftButtonUp += (sender, e) => ViewportCanvas_MouseLeftButtonUp(sender, e, viewportType);
                        
                        // Double-click detection
                        canvas.PreviewMouseLeftButtonDown += (sender, e) => ViewportCanvas_PreviewMouseLeftButtonDown(sender, e, viewportType);
                    }
                }

                App.MainController?.AddLogMessage("> 🖱️ Individual viewport mouse events setup complete");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ Viewport mouse events setup error: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all viewports
        /// </summary>
        public void ClearAllViewports()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // ✅ Dictionary'den canvas'ları al ve temizle
                    foreach (var kvp in _viewportCanvases)
                    {
                        var canvas = kvp.Value;
                        if (canvas != null)
                        {
                            canvas.Children.Clear();
                        }
                    }
                });

                System.Diagnostics.Debug.WriteLine("✅ All viewports cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ClearAllViewports error: {ex.Message}");
            }
        }

        /// <summary>
        /// Get canvas for specific viewport type
        /// </summary>
        public Canvas GetViewportCanvas(ViewportType viewportType)
        {
            return _viewportCanvases.TryGetValue(viewportType, out Canvas canvas) ? canvas : null;
        }

        /// <summary>
        /// Reset zoom and pan for all viewports
        /// </summary>
        public void ResetAllViewports()
        {
            try
            {
                foreach (var viewportType in _viewportZoomFactors.Keys.ToList())
                {
                    _viewportZoomFactors[viewportType] = 1.0;
                    _viewportPanOffsets[viewportType] = new Point(0, 0);
                    ApplyViewportTransform(viewportType);
                }

                App.MainController?.AddLogMessage("> 🔄 All viewports reset to default zoom/pan");
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ All viewports reset error: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply zoom and pan transform to specific viewport
        /// </summary>
        public void ApplyViewportTransform(ViewportType viewportType)
        {
            try
            {
                var canvas = _viewportCanvases[viewportType];
                if (canvas == null) return;

                var transformGroup = new TransformGroup();

                // Add pan transform
                transformGroup.Children.Add(new TranslateTransform(
                    _viewportPanOffsets[viewportType].X,
                    _viewportPanOffsets[viewportType].Y
                ));

                // Add zoom transform (scale from center)
                transformGroup.Children.Add(new ScaleTransform(
                    _viewportZoomFactors[viewportType],
                    _viewportZoomFactors[viewportType],
                    canvas.ActualWidth / 2,
                    canvas.ActualHeight / 2
                ));

                // Apply transform
                canvas.RenderTransform = transformGroup;
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ {viewportType} transform application error: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods

        private void InitializeViewportData(Canvas topCanvas, Canvas frontCanvas, Canvas rightCanvas, Canvas isometricCanvas)
        {
            // Initialize zoom and pan for each viewport
            _viewportZoomFactors = new Dictionary<ViewportType, double>
            {
                { ViewportType.Top, 1.0 },
                { ViewportType.Front, 1.0 },
                { ViewportType.Right, 1.0 },
                { ViewportType.Isometric, 1.0 }
            };

            _viewportPanOffsets = new Dictionary<ViewportType, Point>
            {
                { ViewportType.Top, new Point(0, 0) },
                { ViewportType.Front, new Point(0, 0) },
                { ViewportType.Right, new Point(0, 0) },
                { ViewportType.Isometric, new Point(0, 0) }
            };

            _viewportCanvases = new Dictionary<ViewportType, Canvas>
            {
                { ViewportType.Top, topCanvas },
                { ViewportType.Front, frontCanvas },
                { ViewportType.Right, rightCanvas },
                { ViewportType.Isometric, isometricCanvas }
            };

            _viewportDragStates = new Dictionary<ViewportType, bool>
            {
                { ViewportType.Top, false },
                { ViewportType.Front, false },
                { ViewportType.Right, false },
                { ViewportType.Isometric, false }
            };

            _viewportLastMousePositions = new Dictionary<ViewportType, Point>
            {
                { ViewportType.Top, new Point(0, 0) },
                { ViewportType.Front, new Point(0, 0) },
                { ViewportType.Right, new Point(0, 0) },
                { ViewportType.Isometric, new Point(0, 0) }
            };

            _lastClickTimes = new Dictionary<ViewportType, DateTime>
            {
                { ViewportType.Top, DateTime.MinValue },
                { ViewportType.Front, DateTime.MinValue },
                { ViewportType.Right, DateTime.MinValue },
                { ViewportType.Isometric, DateTime.MinValue }
            };

            // Enable hardware acceleration for all viewports
            foreach (var canvas in _viewportCanvases.Values)
            {
                if (canvas != null)
                {
                    canvas.CacheMode = new BitmapCache();
                }
            }
        }

        // Mouse event handlers (kısaltılmış hali - tam kod çok uzun)
        private void ViewportCanvas_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e, ViewportType viewportType)
        {
            // Mouse wheel zoom implementation
        }

        private void ViewportCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e, ViewportType viewportType)
        {
            // Mouse down implementation
        }

        private void ViewportCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e, ViewportType viewportType)
        {
            // Mouse move implementation
        }

        private void ViewportCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e, ViewportType viewportType)
        {
            // Mouse up implementation
        }

        private void ViewportCanvas_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e, ViewportType viewportType)
        {
            // Double-click detection implementation
        }

        #endregion
    }
}