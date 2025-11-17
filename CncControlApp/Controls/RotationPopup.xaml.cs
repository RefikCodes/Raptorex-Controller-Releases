using System;
using System.Linq;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Globalization;
using CncControlApp.Helpers; // ✅ ADD: Import UiHelper namespace

namespace CncControlApp.Controls
{
    public partial class RotationPopup : Window
    {
        private readonly CncControlApp.GCodeView _gcodeView;
        private DispatcherTimer _redrawDebounceTimer;
        private double _pendingAngle = 0;
        private bool _isInitialLoad = true;

        // Marker management
        private Ellipse _touchPointMarker;
        private Ellipse _machineMarker;
        private DispatcherTimer _machinePositionUpdateTimer;
        private bool _isAnimatingMovement = false;

        // Touch/Click handling for G00 movement
        private double? _lastTouchedMachineX = null;
        private double? _lastTouchedMachineY = null;

        // ✅ FIX: Store toggle state to persist between movements
        private bool _autoZeroEnabled = true; // Default: ON

        // Add missing _touchPointLabel field and remove ClearSelectedCoordinateBoxes usage
        private TextBlock _touchPointLabel; // label showing machine coords near touch marker

        // New field to control zero prompt gating
        private bool _awaitingZeroPrompt = false; // debounce to avoid random/duplicate prompts

        // ✅ ADD: Helper method for logging (eliminates nullable method group issues)
        private void Log(string message) => App.MainController?.AddLogMessage(message);

        public RotationPopup(CncControlApp.GCodeView gcodeView)
        {
            InitializeComponent();
            _gcodeView = gcodeView;

            // Setup debounce timer for smooth rotation
            _redrawDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150) // Redraw after 150ms of no slider movement
            };
            _redrawDebounceTimer.Tick += RedrawDebounceTimer_Tick;

            try
            {
                // Initialize slider to current angle from main view
                double angle = _gcodeView?.GetCurrentRotationAngle() ?? 0;
                RotationSlider.Value = angle;
                AngleValueText.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}°", angle);
                _pendingAngle = angle;
                RefreshApplyResetState();
            }
            catch { }

            try
            {
                // ✅ FIX: Always enable Fit to table (no toggle needed)
                if (_gcodeView != null)
                {
                    _gcodeView.EnableFitOnRotation = true;
                    App.MainController?.AddLogMessage($"> RotationPopup: Fit to table ALWAYS enabled");
                }

                // Ensure the toggle default is applied after UI loads
                // ✅ REFACTORED: Use UiHelper.RunOnUi() instead of manual Dispatcher.BeginInvoke
                UiHelper.RunOnUi(() =>
                {
                    try
                    {
                        // ✅ FIX: Explicitly initialize flag to ensure zero dialog shows on first G00 click
                        _awaitingZeroPrompt = false;
                        Log($"> RotationPopup: Zero prompt flag initialized to FALSE");
                        Log($"> RotationPopup: Auto-zero ALWAYS enabled (no toggle)");
                    }
                    catch { }
                }, DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                Log($"> ❌ RotationPopup constructor init error: {ex.Message}");
            }
        }

        // Coordinate display update
        private void UpdateCoordinateDisplay()
        {
            try
            {
                var status = App.MainController?.MStatus;
                if (status == null) return;
                double mx = status.X;
                double my = status.Y;
                double mz = status.Z;
                double wx = status.WorkX;
                double wy = status.WorkY;
                double wz = status.WorkZ;
                UiHelper.RunOnUi(() =>
                {
                    var workX = this.FindName("WorkXValueText") as TextBox; if (workX != null) workX.Text = wx.ToString("F3", CultureInfo.InvariantCulture);
                    var machX = this.FindName("MachineXValueText") as TextBox; if (machX != null) machX.Text = mx.ToString("F3", CultureInfo.InvariantCulture);
                    var workY = this.FindName("WorkYValueText") as TextBox; if (workY != null) workY.Text = wy.ToString("F3", CultureInfo.InvariantCulture);
                    var machY = this.FindName("MachineYValueText") as TextBox; if (machY != null) machY.Text = my.ToString("F3", CultureInfo.InvariantCulture);
                    var workZ = this.FindName("WorkZValueText") as TextBox; if (workZ != null) workZ.Text = wz.ToString("F3", CultureInfo.InvariantCulture);
                    var machZ = this.FindName("MachineZValueText") as TextBox; if (machZ != null) machZ.Text = mz.ToString("F3", CultureInfo.InvariantCulture);
                });
            }
            catch { }
        }

        private void SubscribeStatusUpdates()
        {
            try
            {
                if (App.MainController?.MStatus != null)
                {
                    App.MainController.MStatus.PropertyChanged -= MStatus_PropertyChanged;
                    App.MainController.MStatus.PropertyChanged += MStatus_PropertyChanged;
                    UpdateCoordinateDisplay();
                }
            }
            catch { }
        }
        private void UnsubscribeStatusUpdates()
        {
            try
            {
                if (App.MainController?.MStatus != null)
                {
                    App.MainController.MStatus.PropertyChanged -= MStatus_PropertyChanged;
                }
            }
            catch { }
        }
        private void MStatus_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(e.PropertyName) ||
                    e.PropertyName == "X" || e.PropertyName == "Y" || e.PropertyName == "Z" ||
                    e.PropertyName == "WorkX" || e.PropertyName == "WorkY" || e.PropertyName == "WorkZ")
                {
                    UpdateCoordinateDisplay();

                    // ✅ While running, keep machine marker in sync with live position
                    if (App.MainController?.MStatus != null)
                    {
                        double mx = App.MainController.MStatus.X;
                        double my = App.MainController.MStatus.Y;
                        var canvasPoint = MachineToCanvasPoint(mx, my);
                        UpdateMachineMarker(canvasPoint);
                    }
                }
            }
            catch { }
        }

        private void TopViewHost_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                _isInitialLoad = false;
                UpdatePopupLiveFitLabel(_pendingAngle);
                SubscribeStatusUpdates();
                if (App.MainController?.MStatus != null)
                {
                    double currentX = App.MainController.MStatus.X;
                    double currentY = App.MainController.MStatus.Y;
                    var canvasPoint = MachineToCanvasPoint(currentX, currentY);
                    if (canvasPoint.HasValue)
                    {
                        UpdateMachineMarker(canvasPoint);
                    }
                }
                UpdateCoordinateDisplay(); // new
                var coordText = this.FindName("TouchedCoordDisplayTextBlock") as TextBlock;
                if (coordText != null) coordText.Text = "Click canvas to select position";
            }
            catch { }
        }

        // Recompute marker positions after any size/redraw
        private void RepositionMarkersAfterResize()
        {
            try
            {
                // Reposition yellow touch marker using stored machine coords
                if (_lastTouchedMachineX.HasValue && _lastTouchedMachineY.HasValue)
                {
                    var touchCanvas = MachineToCanvasPoint(_lastTouchedMachineX.Value, _lastTouchedMachineY.Value);
                    if (touchCanvas.HasValue)
                    {
                        UpdateTouchPointMarker(touchCanvas.Value);
                    }
                }

                // Reposition live machine marker to current machine coords
                if (App.MainController?.MStatus != null)
                {
                    var m = App.MainController.MStatus;
                    var machineCanvas = MachineToCanvasPoint(m.X, m.Y);
                    UpdateMachineMarker(machineCanvas);
                }
            }
            catch { }
        }

        private void TopViewHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                // Redraw on size change (but not during initial load)
                if (!_isInitialLoad)
                {
                    _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);

                    // Ensure overlay matches base canvas size to prevent coordinate drift
                    if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                    {
                        TopViewOverlayCanvas.Width = TopViewCanvas.ActualWidth;
                        TopViewOverlayCanvas.Height = TopViewCanvas.ActualHeight;
                    }

                    // ✅ Update FIT status after redraw
                    UpdatePopupLiveFitLabel(_pendingAngle);

                    // ✅ Reposition markers to new canvas mapping
                    RepositionMarkersAfterResize();
                }
            }
            catch { }
        }

        // Helper to refresh Apply/Reset enable state based on current angle
        private void RefreshApplyResetState()
        {
            try
            {
                double angle = _pendingAngle;
                bool hasRotation = Math.Abs(angle) >0.0001;
                if (ApplyButton != null) ApplyButton.IsEnabled = hasRotation;
                if (ResetButton != null) ResetButton.IsEnabled = hasRotation;
            }
            catch { }
        }

        private void RotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                double angle = e.NewValue;
                if (AngleValueText != null)
                    AngleValueText.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}°", angle);

                _gcodeView?.SetRotationAngle(angle);
                _pendingAngle = angle;
                UpdatePopupLiveFitLabel(angle);
                // Enable/disable Apply/Reset based on angle
                RefreshApplyResetState();

                // Apply instant rotation transform for smooth visual feedback
                // Rotate around G-code origin (machine position on canvas)
                if (TopViewCanvas != null && !_isInitialLoad)
                {
                    var rotateTransform = new RotateTransform { Angle = angle };

                    // Calculate G-code origin position on canvas (where machine currently is)
                    var originPoint = _gcodeView?.GetGCodeOriginCanvasPosition(TopViewCanvas);
                    if (originPoint.HasValue)
                    {
                        // Set rotation center to G-code origin position
                        rotateTransform.CenterX = originPoint.Value.X;
                        rotateTransform.CenterY = originPoint.Value.Y;
                    }
                    else
                    {
                        // Fallback to canvas center if we can't get origin position
                        TopViewCanvas.RenderTransformOrigin = new Point(0.5, 0.5);
                    }

                    TopViewCanvas.RenderTransform = rotateTransform;
                }

                // Debounce the full redraw (expensive operation)
                _redrawDebounceTimer.Stop();
                _redrawDebounceTimer.Start();
            }
            catch { }
        }

        private void RedrawDebounceTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                _redrawDebounceTimer.Stop();

                // Full redraw with overlay after user stops moving slider
                if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                {
                    _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);

                    // ✅ Update FIT status after debounced redraw
                    UpdatePopupLiveFitLabel(_pendingAngle);

                    // ✅ Reposition markers to new canvas mapping
                    RepositionMarkersAfterResize();
                }
            }
            catch { }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _redrawDebounceTimer.Stop(); // Stop any pending redraws
                RotationSlider.Value = 0;
                _pendingAngle =0;
                _gcodeView?.ResetRotation();

                // Immediate redraw on reset
                if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                {
                    _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);

                    // ✅ Update FIT status after reset
                    UpdatePopupLiveFitLabel(0);
                }
            }
            catch { }
            finally
            {
                RefreshApplyResetState();
            }
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Guard UI to avoid re-entrancy
                if (ApplyButton != null) ApplyButton.IsEnabled = false;
                if (GotoTouchedCoordButton != null) GotoTouchedCoordButton.IsEnabled = false; // avoid sending while applying
                _redrawDebounceTimer.Stop();

                // Apply rotation (reparse, rebuild, redraw main views)
                await _gcodeView?.ApplyRotation();

                // Clear any transient transform and reset angle
                if (TopViewCanvas != null) TopViewCanvas.RenderTransform = null;
                RotationSlider.Value =0; // will update angle text and pending angle via ValueChanged
                _pendingAngle = 0;

                // Redraw popup canvas from fresh data
                if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                {
                    _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                    UpdatePopupLiveFitLabel(0);
                }

                // Ensure next G00 shows zero popup if toggle is on
                _awaitingZeroPrompt = false;
            }
            catch (Exception ex)
            {
                App.MainController?.AddLogMessage($"> ❌ ApplyRotation error: {ex.Message}");
            }
            finally
            {
                RefreshApplyResetState();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UnsubscribeStatusUpdates();
                // Clean up animation timer and markers
                StopMachinePositionUpdateTimer();
                if (_machinePositionUpdateTimer != null)
                {
                    _machinePositionUpdateTimer.Tick -= MachinePositionUpdateTimer_Tick;
                    _machinePositionUpdateTimer = null;
                }

                _redrawDebounceTimer?.Stop();
                Close();
            }
            catch { }
        }

        private void HeaderBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    DragMove();
                }
            }
            catch { }
        }

        private void TopViewCanvas_TouchDown(object sender, TouchEventArgs e)
        {
            try
            {
                var p = e.GetTouchPoint(TopViewCanvas).Position;
                HandleTopViewTouch(p);
                e.Handled = true;
            }
            catch { }
        }

        private void TopViewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var p = e.GetPosition(TopViewCanvas);
                HandleTopViewTouch(p);
            }
            catch { }
        }

        private void HandleTopViewTouch(Point canvasPoint)
        {
            try
            {
                if (!TryMapCanvasToMachineCoordinates(canvasPoint, out double machineX, out double machineY))
                    return;

                _lastTouchedMachineX = machineX;
                _lastTouchedMachineY = machineY;

                // Update display via FindName to avoid generated field dependency
                var coordText = this.FindName("TouchedCoordDisplayTextBlock") as TextBlock;
                if (coordText != null)
                {
                    coordText.Text = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "M X:{0:F2} Y:{1:F2}",
                        machineX, machineY);
                }

                // Enable G00 button
                if (GotoTouchedCoordButton != null)
                {
                    GotoTouchedCoordButton.IsEnabled = true;
                }

                // Show touch point marker on canvas
                UpdateTouchPointMarker(canvasPoint);
            }
            catch { }
        }

        /// <summary>
        /// Map canvas point to machine coordinates
        /// </summary>
        private bool TryMapCanvasToMachineCoordinates(Point canvasPoint, out double machineX, out double machineY)
        {
            machineX = 0;
            machineY = 0;

            try
            {
                if (TopViewCanvas == null) return false;
                double canvasWidth = TopViewCanvas.ActualWidth;
                double canvasHeight = TopViewCanvas.ActualHeight;
                if (canvasWidth <= 0 || canvasHeight <= 0) return false;

                if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                    return false;

                var mm = xf.ToMachine(canvasPoint.X, canvasPoint.Y);
                machineX = mm.X;
                machineY = mm.Y;

                // Reject selections outside table bounds
                if (machineX < 0 || machineY < 0 || machineX > xf.MaxX || machineY > xf.MaxY)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Convert a machine coordinate (mm) to canvas point using same mapping as drawing
        /// </summary>
        private Point? MachineToCanvasPoint(double machineX, double machineY)
        {
            try
            {
                UpdatePopupLiveFitLabel(_pendingAngle);
                if (TopViewCanvas == null) return null;

                double canvasWidth = TopViewCanvas.ActualWidth;
                double canvasHeight = TopViewCanvas.ActualHeight;
                if (canvasWidth <= 0 || canvasHeight <= 0) return null;

                if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                    return null;

                var pt = xf.ToCanvas(machineX, machineY);
                return pt;
            }
            catch { return null; }
        }

        /// <summary>
        /// Update live fit status label
        /// </summary>
        private void UpdatePopupLiveFitLabel(double angle)
        {
            try
            {
                if (FitLiveStatusText == null || _gcodeView == null) return;
                var (fits, details) = _gcodeView.CheckLiveFitAtAngle(angle);
                if (fits)
                {
                    FitLiveStatusText.Text = "✔";
                    FitLiveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // green
                }
                else
                {
                    FitLiveStatusText.Text = "✖";
                    FitLiveStatusText.Foreground = new SolidColorBrush(Color.FromRgb(255, 59, 48)); // red
                }
                FitLiveStatusText.ToolTip = details;
            }
            catch { }
        }

        private async void GotoTouchedCoordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_lastTouchedMachineX.HasValue && _lastTouchedMachineY.HasValue && App.MainController?.IsConnected == true)
                {
                    // Start machine position animation
                    StartMachinePositionUpdateTimer();

                    string cmd = string.Format(CultureInfo.InvariantCulture, "G53 G00 X{0:F3} Y{1:F3}", _lastTouchedMachineX.Value, _lastTouchedMachineY.Value);
                    Log($"> Rotation popup G53 jog: {cmd}");
                    bool ok = await App.MainController.SendGCodeCommandWithConfirmationAsync(cmd);

                    // Small deterministic wait, then wait idle
                    await System.Threading.Tasks.Task.Delay(200);
                    await WaitForIdleState();

                    StopMachinePositionUpdateTimer();

                    // ✅ ALWAYS show zero popup after G00 completes
                    bool auto = true; // Always show zero popup (no toggle)
                    Log($"> Post-G00: Auto-zero ALWAYS enabled, cmdOk={ok}");

                    // Show zero popup if not already awaiting
                    if (auto && !_awaitingZeroPrompt)
                    {
                        _awaitingZeroPrompt = true;
                        try
                        {
                            bool userConfirmed = await ShowZeroConfirmationDialog();
                            if (userConfirmed)
                            {
                                Log("> User confirmed zero; sending G10 L20 P0 X0 Y0");
                                string zeroCmd = "G10 L20 P0 X0 Y0";
                                bool zeroSuccess = await App.MainController.SendGCodeCommandWithConfirmationAsync(zeroCmd);
                                if (zeroSuccess)
                                {
                                    await System.Threading.Tasks.Task.Delay(300);
                                    try
                                    {
                                        await App.MainController.SendGCodeCommandAsync("?");
                                        Log("> Status requested after zeroing");
                                        await System.Threading.Tasks.Task.Delay(200);
                                    }
                                    catch (Exception statusEx) { Log($"> ⚠️ ERROR sending status request: {statusEx.Message}"); }
                                    try
                                    {
                                        var disp = Application.Current?.Dispatcher;
                                        disp?.Invoke(new Action(() =>
                                        {
                                            try
                                            {
                                                var field = _gcodeView?.GetType().GetField("_fileService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                var fileService = field?.GetValue(_gcodeView);
                                                var redrawMethod = fileService?.GetType().GetMethod("RedrawAllViewports");
                                                redrawMethod?.Invoke(fileService, null);
                                                Log("> ✅ Main view redrawn");
                                            }
                                            catch (Exception innerEx) { Log($"> ⚠️ Main view redraw error: {innerEx.Message}"); }
                                        }), DispatcherPriority.Send);
                                    }
                                    catch (Exception redrawEx) { Log($"> ⚠️ Main view redraw error: {redrawEx.Message}"); }
                                    try
                                    {
                                        var disp2 = Application.Current?.Dispatcher;
                                        disp2?.Invoke(new Action(() =>
                                        {
                                            if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                                            {
                                                _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                                                Log("> ✅ Popup view redrawn");
                                            }
                                        }), DispatcherPriority.Send);
                                    }
                                    catch (Exception popupRedrawEx) { Log($"> ⚠️ Popup view redraw error: {popupRedrawEx.Message}"); }
                                }
                                else
                                {
                                    Log($"> ❌ Failed to set X and Y to zero");
                                }
                            }
                            else
                            {
                                Log($"> User cancelled zero setting");
                            }
                        }
                        finally
                        {
                            _awaitingZeroPrompt = false;
                        }
                    }

                    // Redraw canvas after movement completes
                    // ✅ REFACTORED: Use UiHelper.RunOnUi() instead of Dispatcher.InvokeAsync
                    UiHelper.RunOnUi(() =>
                    {
                        if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                        {
                            Log($"> Redrawing popup canvas after G53 movement");
                            _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                        }
                    });

                    // Redraw canvas after movement completes
                    var disp3 = Application.Current?.Dispatcher;
                    try
                    {
                        disp3?.Invoke(new Action(() =>
                        {
                            if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                            {
                                Log("> Redrawing popup canvas after G53 movement");
                                _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                            }
                        }), DispatcherPriority.Send);
                    }
                    catch (Exception invEx) { Log($"> ⚠️ Redraw invoke error: {invEx.Message}"); }
                }
            }
            catch (Exception ex)
            {
                StopMachinePositionUpdateTimer();
                Log($"> ❌ ERROR: Rotation popup G53 jog - {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task<bool> ShowZeroConfirmationDialog()
        {
            try
            {
                string message = string.Format(CultureInfo.InvariantCulture,
                    "Set X and Y to permanent zero at this position?\n\nMachine Position:\n X: {0:F3} mm\n Y: {1:F3} mm\n\nThis will execute: G10 L20 P0 X0 Y0\n(Permanent zero stored in EEPROM)",
                    _lastTouchedMachineX.GetValueOrDefault(), _lastTouchedMachineY.GetValueOrDefault());
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess())
                    return MessageDialog.ShowConfirm("Set Zero Confirmation", message);
                return disp.Invoke(new Func<bool>(() => MessageDialog.ShowConfirm("Set Zero Confirmation", message)), DispatcherPriority.Send);
            }
            catch (Exception ex)
            {
                Log($"> ❌ ERROR in ShowZeroConfirmationDialog: {ex.Message}");
                return false;
            }
        }

        private async System.Threading.Tasks.Task WaitForIdleState()
        {
            var controller = App.MainController; if (controller == null) return;
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            bool seenNonIdle = false;
            PropertyChangedEventHandler handler = (s, e) =>
            {
                try
                {
                    if (e.PropertyName != nameof(controller.MachineStatus)) return;
                    string state = controller.MachineStatus ?? string.Empty;
                    if (state.StartsWith("Alarm", StringComparison.OrdinalIgnoreCase)) { tcs.TrySetResult(false); return; }
                    if (state.StartsWith("Run", StringComparison.OrdinalIgnoreCase) || state.StartsWith("Jog", StringComparison.OrdinalIgnoreCase)) { seenNonIdle = true; return; }
                    if (state.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) && seenNonIdle) tcs.TrySetResult(true);
                }
                catch { }
            };
            try
            {
                controller.PropertyChanged += handler;
                string init = controller.MachineStatus ?? string.Empty;
                if (init.StartsWith("Run", StringComparison.OrdinalIgnoreCase) || init.StartsWith("Jog", StringComparison.OrdinalIgnoreCase)) seenNonIdle = true;
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(1200).ConfigureAwait(false);
                        if (!seenNonIdle)
                        {
                            string s2 = controller.MachineStatus ?? string.Empty;
                            if (s2.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)) tcs.TrySetResult(true);
                        }
                    }
                    catch { }
                });
                using (var cts = new System.Threading.CancellationTokenSource(30000))
                using (cts.Token.Register(() => tcs.TrySetCanceled(), false))
                    await tcs.Task.ConfigureAwait(false);
            }
            catch { }
            finally { controller.PropertyChanged -= handler; }
        }

        #region Marker Management
        private void UpdateTouchPointMarker(Point touchPoint)
        {
            try
            {
                if (TopViewOverlayCanvas == null) return;
                if (_touchPointMarker != null) TopViewOverlayCanvas.Children.Remove(_touchPointMarker);
                if (_touchPointLabel != null) TopViewOverlayCanvas.Children.Remove(_touchPointLabel);
                _touchPointMarker = new Ellipse { Width =20, Height =20, Stroke = new SolidColorBrush(Colors.Gold), StrokeThickness =3, Fill = new SolidColorBrush(Color.FromArgb(100,255,215,0)), IsHitTestVisible = false };
                Canvas.SetLeft(_touchPointMarker, touchPoint.X -10);
                Canvas.SetTop(_touchPointMarker, touchPoint.Y -10);
                Canvas.SetZIndex(_touchPointMarker,1000);
                TopViewOverlayCanvas.Children.Add(_touchPointMarker);
                if (_lastTouchedMachineX.HasValue && _lastTouchedMachineY.HasValue)
                {
                    _touchPointLabel = new TextBlock { Text = $"X:{_lastTouchedMachineX.Value:F2}\nY:{_lastTouchedMachineY.Value:F2}", Foreground = new SolidColorBrush(Colors.Gold), FontFamily = new FontFamily("Consolas"), FontSize =12, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromArgb(160,0,0,0)), Padding = new Thickness(4), TextAlignment = TextAlignment.Center };
                    Canvas.SetLeft(_touchPointLabel, touchPoint.X +14);
                    Canvas.SetTop(_touchPointLabel, touchPoint.Y -6);
                    Canvas.SetZIndex(_touchPointLabel,1001);
                    TopViewOverlayCanvas.Children.Add(_touchPointLabel);
                }
                var pulseAnimation = new DoubleAnimation { From =1.0, To =0.6, Duration = TimeSpan.FromMilliseconds(500), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                _touchPointMarker.BeginAnimation(UIElement.OpacityProperty, pulseAnimation);
            }
            catch { }
        }
        private void ClearTouchPointMarker()
        {
            try
            {
                if (_touchPointMarker != null && TopViewOverlayCanvas != null) { TopViewOverlayCanvas.Children.Remove(_touchPointMarker); _touchPointMarker = null; }
                if (_touchPointLabel != null && TopViewOverlayCanvas != null) { TopViewOverlayCanvas.Children.Remove(_touchPointLabel); _touchPointLabel = null; }
            }
            catch { }
        }
        private void UpdateMachineMarker(Point? machinePoint)
        {
            try
            {
                if (TopViewOverlayCanvas == null) return;
                if (!machinePoint.HasValue)
                {
                    if (_machineMarker != null) { TopViewOverlayCanvas.Children.Remove(_machineMarker); _machineMarker = null; }
                    return;
                }
                if (_machineMarker != null) TopViewOverlayCanvas.Children.Remove(_machineMarker);
                _machineMarker = new Ellipse { Width =18, Height =18, Stroke = new SolidColorBrush(Colors.Lime), StrokeThickness =3, Fill = new SolidColorBrush(Color.FromArgb(120,0,255,0)), IsHitTestVisible = false };
                Canvas.SetLeft(_machineMarker, machinePoint.Value.X -9);
                Canvas.SetTop(_machineMarker, machinePoint.Value.Y -9);
                Canvas.SetZIndex(_machineMarker,999);
                TopViewOverlayCanvas.Children.Add(_machineMarker);
            }
            catch { }
        }
        private void StartMachinePositionUpdateTimer()
        {
            try
            {
                if (_machinePositionUpdateTimer == null)
                {
                    _machinePositionUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                    _machinePositionUpdateTimer.Tick += MachinePositionUpdateTimer_Tick;
                }
                if (!_machinePositionUpdateTimer.IsEnabled) { _isAnimatingMovement = true; _machinePositionUpdateTimer.Start(); }
            }
            catch { }
        }
        private void StopMachinePositionUpdateTimer()
        {
            try
            {
                if (_machinePositionUpdateTimer != null && _machinePositionUpdateTimer.IsEnabled) { _machinePositionUpdateTimer.Stop(); _isAnimatingMovement = false; }
            }
            catch { }
        }
        private void MachinePositionUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (App.MainController?.MStatus == null) return;
                double currentX = App.MainController.MStatus.X;
                double currentY = App.MainController.MStatus.Y;
                var canvasPoint = MachineToCanvasPoint(currentX, currentY);
                if (canvasPoint.HasValue)
                {
                    UpdateMachineMarker(canvasPoint);
                    UpdateCoordinateDisplay();
                    if (_lastTouchedMachineX.HasValue && _lastTouchedMachineY.HasValue)
                    {
                        double dx = Math.Abs(currentX - _lastTouchedMachineX.Value);
                        double dy = Math.Abs(currentY - _lastTouchedMachineY.Value);
                        if (Math.Sqrt(dx * dx + dy * dy) <0.5)
                        {
                            ClearTouchPointMarker();
                            _lastTouchedMachineX = null; _lastTouchedMachineY = null;
                            if (GotoTouchedCoordButton != null) GotoTouchedCoordButton.IsEnabled = false;
                            var coordText = this.FindName("TouchedCoordDisplayTextBlock") as TextBlock;
                            if (coordText != null) coordText.Text = "Click canvas to select position";
                        }
                    }
                }
            }
            catch { }
        }
        #endregion

        private void RexsetButton_Click(object sender, RoutedEventArgs e)
        {
            try { App.MainController?.AddLogMessage("> Rexset action triggered from RotationPopup"); } catch { }
        }
    }
}
