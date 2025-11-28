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

        // Constant values
        private const double SafeZLiftMm = 5.0; // Z lift before XY rapid if near surface
        private const double TouchArrivalThresholdMm = 0.5; // distance to consider arrived at touched point

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
                bool hasRotation = Math.Abs(angle) > 0.0001;
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
                _pendingAngle = 0;
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
                RotationSlider.Value = 0; // will update angle text and pending angle via ValueChanged
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
                // Homing kontrolü kaldırıldı: HOMING yapılmamış olsa da hareket engellenmeyecek

                if (_lastTouchedMachineX.HasValue && _lastTouchedMachineY.HasValue && App.MainController?.IsConnected == true)
                    if (!_lastTouchedMachineX.HasValue || !_lastTouchedMachineY.HasValue)
                    {
                        Log("> ⚠️ No touched point set – ignoring jog request");
                        return;
                    }
                if (App.MainController?.IsConnected != true)
                {
                    Log("> ❌ Not connected – cannot jog");
                    return;
                }

                // Current status snapshot
                var mStatus = App.MainController.MStatus;
                double currentMachineX = mStatus?.X ?? 0; // machine coords
                double currentMachineY = mStatus?.Y ?? 0;
                double currentWorkX = mStatus?.WorkX ?? 0; // work coords
                double currentWorkY = mStatus?.WorkY ?? 0;
                double currentWorkZ = mStatus?.WorkZ ?? 0;

                // Selected point assumed MACHINE coordinates (produced by WorkspaceTransform.ToMachine)
                double targetMachineX = _lastTouchedMachineX.Value;
                double targetMachineY = _lastTouchedMachineY.Value;

                // Clamp to table limits (settings $130/$131)
                double tableMaxX = double.MaxValue;
                double tableMaxY = double.MaxValue;
                try
                {
                    var xLimit = App.MainController.Settings?.FirstOrDefault(s => s.Id == 130);
                    var yLimit = App.MainController.Settings?.FirstOrDefault(s => s.Id == 131);
                    if (xLimit != null && double.TryParse(xLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double limX)) tableMaxX = limX;
                    if (yLimit != null && double.TryParse(yLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double limY)) tableMaxY = limY;
                }
                catch { }

                if (targetMachineX < 0) { Log($"> ⚠️ Clamping X {targetMachineX:F3} to 0"); targetMachineX = 0; }
                if (targetMachineY < 0) { Log($"> ⚠️ Clamping Y {targetMachineY:F3} to 0"); targetMachineY = 0; }
                if (targetMachineX > tableMaxX) { Log($"> ⚠️ Clamping X {targetMachineX:F3} to {tableMaxX:F3}"); targetMachineX = tableMaxX; }
                if (targetMachineY > tableMaxY) { Log($"> ⚠️ Clamping Y {targetMachineY:F3} to {tableMaxY:F3}"); targetMachineY = tableMaxY; }

                Log($"> Jog target (machine): X={targetMachineX:F3} Y={targetMachineY:F3} | Current MPos: X={currentMachineX:F3} Y={currentMachineY:F3} | WPos: X={currentWorkX:F3} Y={currentWorkY:F3}");

                // Ensure absolute mode before issuing rapid (G53 uses machine but we still normalize modal state)
                bool modalOk = await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
                if (!modalOk) Log("> ⚠️ Failed to enforce G90 (absolute) – continuing anyway");

                // Optional safe Z lift if near work surface (simple heuristic)
                if (currentWorkZ < 1.0)
                {
                    string liftCmd = $"G91 G00 Z{SafeZLiftMm:F3}"; // relative lift
                    Log($"> Performing safe Z lift: {liftCmd}");
                    bool liftOk = await App.MainController.SendGCodeCommandWithConfirmationAsync(liftCmd);
                    await System.Threading.Tasks.Task.Delay(100);
                    // Return to absolute mode (for following commands) – G53 unaffected by G91 but be explicit
                    await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
                    if (!liftOk) Log("> ⚠️ Safe Z lift failed");
                }

                // Build machine rapid command using G53 (machine coordinate system)
                string cmd = string.Format(CultureInfo.InvariantCulture, "G53 G00 X{0:F3} Y{1:F3}", targetMachineX, targetMachineY);
                Log($"> Rotation popup G53 rapid: {cmd}");

                // Start animation timer BEFORE sending so we show motion
                StartMachinePositionUpdateTimer();

                bool ok = await App.MainController.SendGCodeCommandWithConfirmationAsync(cmd);
                if (!ok)
                {
                    Log("> ❌ Jog command failed – aborting zero prompt");
                    StopMachinePositionUpdateTimer();
                    return;
                }

                // Always wait deterministically for Idle after movement completes
                await WaitForIdleState();

                StopMachinePositionUpdateTimer();

                // Redraw popup canvas BEFORE asking for Zero confirmation – ensures visual state is final
                UiHelper.RunOnUi(() =>
                {
                    if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                    {
                        Log("> Redraw after jog and before zero confirm");
                        _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                        UpdatePopupLiveFitLabel(_pendingAngle);
                        RepositionMarkersAfterResize();
                    }
                }, DispatcherPriority.Send);

                // Decide zero prompt based on toggle field
                if (_autoZeroEnabled && !_awaitingZeroPrompt)
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
                                    await System.Threading.Tasks.Task.Delay(150);
                                }
                                catch (Exception statusEx) { Log($"> ⚠️ Status request error: {statusEx.Message}"); }

                                // Redraw main viewports
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
                                            Log("> ✅ Main viewports redrawn");
                                        }
                                        catch (Exception innerEx) { Log($"> ⚠️ Main view redraw error: {innerEx.Message}"); }
                                    }), DispatcherPriority.Send);
                                }
                                catch (Exception redrawEx) { Log($"> ⚠️ Main view redraw dispatch error: {redrawEx.Message}"); }

                                // Redraw popup
                                try
                                {
                                    if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                                    {
                                        _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                                        Log("> ✅ Popup view redrawn");
                                    }
                                }
                                catch (Exception popupEx) { Log($"> ⚠️ Popup redraw error: {popupEx.Message}"); }
                            }
                            else
                            {
                                Log("> ❌ Failed to set X/Y zero (G10)");
                            }
                        }
                        else
                        {
                            Log("> User cancelled zero");
                        }
                    }
                    catch (Exception zeroPromptEx)
                    {
                        Log($"> ❌ Zero dialog error: {zeroPromptEx.Message}");
                    }
                    finally
                    {
                        _awaitingZeroPrompt = false; // always release gate
                    }
                }

                // Final popup redraw (single occurrence – removed duplicates)
                UiHelper.RunOnUi(() =>
                {
                    if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                    {
                        Log("> Final redraw after jog");
                        _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                    }
                });
            }
            catch (Exception ex)
            {
                StopMachinePositionUpdateTimer();
                _awaitingZeroPrompt = false; // ensure gate released on unexpected errors
                Log($"> ❌ ERROR during jog sequence: {ex.Message}");
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
                _touchPointMarker = new Ellipse { Width = 20, Height = 20, Stroke = new SolidColorBrush(Colors.Gold), StrokeThickness = 3, Fill = new SolidColorBrush(Color.FromArgb(100, 255, 215, 0)), IsHitTestVisible = false };
                Canvas.SetLeft(_touchPointMarker, touchPoint.X - 10);
                Canvas.SetTop(_touchPointMarker, touchPoint.Y - 10);
                Canvas.SetZIndex(_touchPointMarker, 1000);
                TopViewOverlayCanvas.Children.Add(_touchPointMarker);
                if (_lastTouchedMachineX.HasValue && _lastTouchedMachineY.HasValue)
                {
                    _touchPointLabel = new TextBlock { Text = $"X:{_lastTouchedMachineX.Value:F2}\nY:{_lastTouchedMachineY.Value:F2}", Foreground = new SolidColorBrush(Colors.Gold), FontFamily = new FontFamily("Consolas"), FontSize = 12, FontWeight = FontWeights.Bold, Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), Padding = new Thickness(4), TextAlignment = TextAlignment.Center };
                    Canvas.SetLeft(_touchPointLabel, touchPoint.X + 14);
                    Canvas.SetTop(_touchPointLabel, touchPoint.Y - 6);
                    Canvas.SetZIndex(_touchPointLabel, 1001);
                    TopViewOverlayCanvas.Children.Add(_touchPointLabel);
                }
                var pulseAnimation = new DoubleAnimation { From = 1.0, To = 0.6, Duration = TimeSpan.FromMilliseconds(500), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
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
                _machineMarker = new Ellipse { Width = 18, Height = 18, Stroke = new SolidColorBrush(Colors.Lime), StrokeThickness = 3, Fill = new SolidColorBrush(Color.FromArgb(120, 0, 255, 0)), IsHitTestVisible = false };
                Canvas.SetLeft(_machineMarker, machinePoint.Value.X - 9);
                Canvas.SetTop(_machineMarker, machinePoint.Value.Y - 9);
                Canvas.SetZIndex(_machineMarker, 999);
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
                        if (Math.Sqrt(dx * dx + dy * dy) < TouchArrivalThresholdMm)
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
