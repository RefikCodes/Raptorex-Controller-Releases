using System;
using System.Collections.Generic;
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

        // ✅ Pan (drag) support for canvas (using mouse events - touch promoted to mouse)
        private bool _isPanning = false;
        private Point _panStartPoint;
        private double _panOffsetX = 0;
        private double _panOffsetY = 0;
        private DateTime _lastFitCheckTime = DateTime.MinValue; // Throttle fit checks during pan

        // ✅ Store original state for cancel/close restore
        private double _originalRotationAngle = 0; // Rotation angle when popup opened
        private bool _changesApplied = false; // True if user applied changes (don't restore on close)

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
                
                // ✅ Store original state for restore on cancel/close
                _originalRotationAngle = angle;
                _changesApplied = false;
                
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

        // Helper to refresh Apply/Reset enable state based on current state
        private void RefreshApplyResetState()
        {
            try
            {
                double angle = _pendingAngle;
                bool hasRotation = Math.Abs(angle) > 0.0001;
                
                // Check pan from transform values directly
                double panX = GCodePanTransform?.X ?? 0;
                double panY = GCodePanTransform?.Y ?? 0;
                bool hasPan = Math.Abs(panX) > 0.1 || Math.Abs(panY) > 0.1;
                
                // ApplyAllChangesButton - enabled if any change exists (rotation or pan)
                if (ApplyAllChangesButton != null) ApplyAllChangesButton.IsEnabled = hasRotation || hasPan;
                
                // Hidden buttons - keep them in sync for backward compatibility
                if (ApplyButton != null) ApplyButton.IsEnabled = hasRotation;
                if (GotoTouchedCoordButton != null) GotoTouchedCoordButton.IsEnabled = hasPan;
                
                // Reset button - enabled if any change exists (rotation or pan)
                if (ResetButton != null) ResetButton.IsEnabled = hasRotation || hasPan;
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
                if (TopViewCanvas != null && !_isInitialLoad && GCodeRotateTransform != null)
                {
                    // Calculate G-code origin position on canvas (where machine currently is)
                    var originPoint = _gcodeView?.GetGCodeOriginCanvasPosition(TopViewCanvas);
                    if (originPoint.HasValue)
                    {
                        // Set rotation center to G-code origin position
                        GCodeRotateTransform.CenterX = originPoint.Value.X;
                        GCodeRotateTransform.CenterY = originPoint.Value.Y;
                    }
                    else
                    {
                        // Fallback to canvas center if we can't get origin position
                        GCodeRotateTransform.CenterX = TopViewCanvas.ActualWidth / 2;
                        GCodeRotateTransform.CenterY = TopViewCanvas.ActualHeight / 2;
                    }

                    GCodeRotateTransform.Angle = angle;
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
                Log("> Reset ALL: rotation, pan, marker");
                _redrawDebounceTimer.Stop(); // Stop any pending redraws
                
                // 1. Reset rotation
                RotationSlider.Value = 0;
                _pendingAngle = 0;
                _gcodeView?.ResetRotation();

                // 2. Reset pan offset
                ResetPan();

                // 3. Reset marker (touch point)
                _lastTouchedMachineX = null;
                _lastTouchedMachineY = null;
                if (_touchPointMarker != null)
                {
                    TopViewOverlayCanvas?.Children.Remove(_touchPointMarker);
                    _touchPointMarker = null;
                }
                if (_touchPointLabel != null)
                {
                    TopViewOverlayCanvas?.Children.Remove(_touchPointLabel);
                    _touchPointLabel = null;
                }

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

                // Clear rotation transform and reset angle
                if (GCodeRotateTransform != null)
                {
                    GCodeRotateTransform.Angle = 0;
                }
                RotationSlider.Value = 0; // will update angle text and pending angle via ValueChanged
                _pendingAngle = 0;

                // Also reset pan offset - the rotation is now baked into GCode
                ResetPan();

                // Redraw popup canvas from fresh data
                if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                {
                    _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                    UpdatePopupLiveFitLabel(0);
                }

                // Mark that changes were applied (don't restore on close)
                _changesApplied = true;

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

        /// <summary>
        /// Unified button that applies all changes in sequence:
        /// 1. Move spindle to panned position (if pan exists)
        /// 2. Ask for zero confirmation
        /// 3. Apply rotation to GCode (if rotation exists)
        /// </summary>
        private async void ApplyAllChangesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button to prevent re-entrancy
                if (ApplyAllChangesButton != null) ApplyAllChangesButton.IsEnabled = false;
                if (ResetButton != null) ResetButton.IsEnabled = false;
                _redrawDebounceTimer.Stop();

                double panX = GCodePanTransform?.X ?? 0;
                double panY = GCodePanTransform?.Y ?? 0;
                bool hasPan = Math.Abs(panX) > 0.1 || Math.Abs(panY) > 0.1;
                bool hasRotation = Math.Abs(_pendingAngle) > 0.0001;

                Log($"> ApplyAllChanges: hasPan={hasPan}, hasRotation={hasRotation}, angle={_pendingAngle:F1}°");

                // ═══════════════════════════════════════════════════════════
                // STEP 1: Move spindle if pan exists
                // ═══════════════════════════════════════════════════════════
                if (hasPan)
                {
                    if (App.MainController?.IsConnected != true)
                    {
                        Log("> ❌ Not connected – cannot move");
                        return;
                    }

                    double canvasWidth = TopViewCanvas?.ActualWidth ?? 0;
                    double canvasHeight = TopViewCanvas?.ActualHeight ?? 0;
                    if (canvasWidth <= 0 || canvasHeight <= 0)
                    {
                        Log("> ⚠️ Invalid canvas size");
                        return;
                    }

                    if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                    {
                        Log("> ⚠️ Cannot create workspace transform");
                        return;
                    }

                    var mStatus = App.MainController.MStatus;
                    double currentMachineX = mStatus?.X ?? 0;
                    double currentMachineY = mStatus?.Y ?? 0;
                    double currentWorkZ = mStatus?.WorkZ ?? 0;

                    // Convert pan to mm
                    double panMmX = panX / xf.Scale;
                    double panMmY = -panY / xf.Scale;

                    double targetMachineX = currentMachineX + panMmX;
                    double targetMachineY = currentMachineY + panMmY;

                    // Clamp to table limits
                    double tableMaxX = xf.MaxX;
                    double tableMaxY = xf.MaxY;
                    if (targetMachineX < 0) targetMachineX = 0;
                    if (targetMachineY < 0) targetMachineY = 0;
                    if (targetMachineX > tableMaxX) targetMachineX = tableMaxX;
                    if (targetMachineY > tableMaxY) targetMachineY = tableMaxY;

                    Log($"> Moving: ({currentMachineX:F3}, {currentMachineY:F3}) -> ({targetMachineX:F3}, {targetMachineY:F3})");

                    await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");

                    if (currentWorkZ < 1.0)
                    {
                        string liftCmd = $"G91 G00 Z{SafeZLiftMm:F3}";
                        await App.MainController.SendGCodeCommandWithConfirmationAsync(liftCmd);
                        await System.Threading.Tasks.Task.Delay(100);
                        await App.MainController.SendGCodeCommandWithConfirmationAsync("G90");
                    }

                    string cmd = string.Format(CultureInfo.InvariantCulture, "G53 G00 X{0:F3} Y{1:F3}", targetMachineX, targetMachineY);
                    Log($"> Moving spindle: {cmd}");

                    StartMachinePositionUpdateTimer();
                    bool moveOk = await App.MainController.SendGCodeCommandWithConfirmationAsync(cmd);
                    if (!moveOk)
                    {
                        Log("> ❌ Move failed");
                        StopMachinePositionUpdateTimer();
                        return;
                    }

                    await WaitForIdleState();
                    StopMachinePositionUpdateTimer();

                    // IMPORTANT: The canvas shows GCode drawn relative to OLD spindle position.
                    // Pan transform was offsetting it to look correct on screen.
                    // Now spindle moved to new position, we need to:
                    // 1. Reset pan (so GCode is not offset)
                    // 2. REDRAW canvas with new spindle position as origin
                    // 3. Then update rotation center
                    
                    // First reset pan transform
                    ResetPan();
                    
                    // Now redraw canvas - this draws GCode relative to NEW spindle position
                    if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                    {
                        _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                    }
                    
                    // Update rotation center to new spindle position
                    if (GCodeRotateTransform != null && hasRotation)
                    {
                        double cw = TopViewCanvas?.ActualWidth ?? 0;
                        double ch = TopViewCanvas?.ActualHeight ?? 0;
                        if (cw > 0 && ch > 0 &&
                            CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(cw, ch, out var xf2))
                        {
                            var newOriginPt = xf2.ToCanvas(targetMachineX, targetMachineY);
                            GCodeRotateTransform.CenterX = newOriginPt.X;
                            GCodeRotateTransform.CenterY = newOriginPt.Y;
                        }
                    }
                    
                    UpdatePopupLiveFitLabel(_pendingAngle);
                    
                    // Wait for UI to complete rendering before showing dialog
                    await System.Threading.Tasks.Task.Delay(50);
                }

                // ═══════════════════════════════════════════════════════════
                // STEP 2: Zero confirmation dialog
                // ═══════════════════════════════════════════════════════════
                if (hasPan && App.MainController?.IsConnected == true)
                {
                    bool userConfirmed = await ShowZeroConfirmationDialog();
                    if (userConfirmed)
                    {
                        Log("> User confirmed zero; sending G10 L20 P0 X0 Y0");
                        bool zeroOk = await App.MainController.SendGCodeCommandWithConfirmationAsync("G10 L20 P0 X0 Y0");
                        if (zeroOk)
                        {
                            await System.Threading.Tasks.Task.Delay(300);
                            try { await App.MainController.SendGCodeCommandAsync("?"); } catch { }
                            await System.Threading.Tasks.Task.Delay(150);
                        }
                    }
                    else
                    {
                        Log("> User cancelled zero");
                    }
                }

                // ═══════════════════════════════════════════════════════════
                // STEP 3: Apply rotation if exists
                // ═══════════════════════════════════════════════════════════
                if (hasRotation)
                {
                    Log($"> Applying rotation: {_pendingAngle:F1}°");
                    await _gcodeView?.ApplyRotation();

                    // Clear rotation transform
                    if (GCodeRotateTransform != null)
                    {
                        GCodeRotateTransform.Angle = 0;
                    }
                    RotationSlider.Value = 0;
                    _pendingAngle = 0;
                }

                // ═══════════════════════════════════════════════════════════
                // STEP 4: Final redraw
                // ═══════════════════════════════════════════════════════════
                UiHelper.RunOnUi(() =>
                {
                    if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                    {
                        _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                        UpdatePopupLiveFitLabel(0);
                    }

                    // Also refresh main viewports
                    try
                    {
                        var field = _gcodeView?.GetType().GetField("_fileService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var fileService = field?.GetValue(_gcodeView);
                        var redrawMethod = fileService?.GetType().GetMethod("RedrawAllViewports");
                        redrawMethod?.Invoke(fileService, null);
                    }
                    catch { }
                }, DispatcherPriority.Send);

                Log("> ✅ All changes applied successfully");
                _changesApplied = true; // Mark that changes were applied (don't restore on close)
                _awaitingZeroPrompt = false;
            }
            catch (Exception ex)
            {
                Log($"> ❌ ApplyAllChanges error: {ex.Message}");
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

                // ✅ If changes were NOT applied, restore original state
                if (!_changesApplied)
                {
                    RestoreOriginalState();
                }

                Close();
            }
            catch { }
        }

        /// <summary>
        /// Restores the original state when popup is closed without applying changes.
        /// Resets rotation angle in GCodeView and triggers main canvas redraw.
        /// </summary>
        private void RestoreOriginalState()
        {
            try
            {
                Log($"> RotationPopup: Restoring original state (angle: {_originalRotationAngle:F1}°)");

                // 1. Restore rotation angle in GCodeView
                if (_gcodeView != null)
                {
                    _gcodeView.SetRotationAngle(_originalRotationAngle);
                }

                // 2. Trigger main canvas redraw with original rotation
                try
                {
                    var field = _gcodeView?.GetType().GetField("_fileService", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var fileService = field?.GetValue(_gcodeView);
                    var redrawMethod = fileService?.GetType().GetMethod("RedrawAllViewports");
                    redrawMethod?.Invoke(fileService, null);
                }
                catch { }

                Log($"> ✅ Original state restored");
            }
            catch (Exception ex)
            {
                Log($"> ❌ RestoreOriginalState error: {ex.Message}");
            }
        }

        #region Canvas Pan (Drag) Support
        // Touch events are promoted to mouse events via Stylus.IsPressAndHoldEnabled="False"
        // This avoids touch capture issues that block other UI elements

        private void TopViewCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Get mouse point relative to the interaction canvas
                var mousePoint = e.GetPosition(TopViewInteractionCanvas);
                
                // Check if mouse is within canvas bounds
                if (mousePoint.X < 0 || mousePoint.Y < 0 || 
                    mousePoint.X > TopViewInteractionCanvas.ActualWidth || 
                    mousePoint.Y > TopViewInteractionCanvas.ActualHeight)
                {
                    // Mouse is outside canvas bounds, don't handle
                    return;
                }
                
                // Mouse is inside canvas - start pan mode
                _isPanning = true;
                _panStartPoint = e.GetPosition(TopViewHost);
                TopViewInteractionCanvas.CaptureMouse();
                e.Handled = true;
            }
            catch { }
        }

        private void TopViewCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                // Pan when left button is pressed
                if (_isPanning && e.LeftButton == MouseButtonState.Pressed)
                {
                    var currentPoint = e.GetPosition(TopViewHost);
                    double deltaX = currentPoint.X - _panStartPoint.X;
                    double deltaY = currentPoint.Y - _panStartPoint.Y;

                    ApplyPanOffset(_panOffsetX + deltaX, _panOffsetY + deltaY);
                    _panStartPoint = currentPoint;
                    _panOffsetX = GCodePanTransform.X;
                    _panOffsetY = GCodePanTransform.Y;

                    // Throttled fit check during pan (every 100ms max)
                    var now = DateTime.Now;
                    if ((now - _lastFitCheckTime).TotalMilliseconds > 100)
                    {
                        _lastFitCheckTime = now;
                        UpdatePopupLiveFitLabel(_pendingAngle);
                    }
                }
            }
            catch { }
        }

        private void TopViewCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_isPanning)
                {
                    _isPanning = false;
                    TopViewInteractionCanvas.ReleaseMouseCapture();
                    RefreshApplyResetState(); // Update button states after pan
                    UpdatePopupLiveFitLabel(_pendingAngle); // Final fit check after pan ends
                }
            }
            catch { }
        }

        private void TopViewCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Right-click to reset pan
                ResetPan();
                e.Handled = true;
            }
            catch { }
        }
        private void ApplyPanOffset(double x, double y)
        {
            try
            {
                // Only pan the GCode canvas, not the overlay (table borders, markers stay fixed)
                if (GCodePanTransform != null)
                {
                    GCodePanTransform.X = x;
                    GCodePanTransform.Y = y;
                }
            }
            catch { }
        }

        private void ResetPan()
        {
            _panOffsetX = 0;
            _panOffsetY = 0;
            _isPanning = false;
            ApplyPanOffset(0, 0);
        }

        private void PanResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ResetPan();
                Log("> Pan reset");
            }
            catch { }
        }

        #endregion

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

                // Show touch point marker on canvas
                UpdateTouchPointMarker(canvasPoint);
                
                // Update all button states (marker, pan, rotation)
                RefreshApplyResetState();
            }
            catch { }
        }

        /// <summary>
        /// Map canvas point to machine coordinates (accounting for pan and rotation transforms)
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

                // Adjust for pan offset - the GCode canvas has been translated
                // So we need to reverse that translation on the click point
                double adjustedX = canvasPoint.X - _panOffsetX;
                double adjustedY = canvasPoint.Y - _panOffsetY;

                // If there's rotation, we need to reverse it too
                if (GCodeRotateTransform != null && Math.Abs(GCodeRotateTransform.Angle) > 0.001)
                {
                    double angle = -GCodeRotateTransform.Angle * Math.PI / 180.0; // Reverse rotation
                    double centerX = GCodeRotateTransform.CenterX;
                    double centerY = GCodeRotateTransform.CenterY;

                    // Translate to origin, rotate, translate back
                    double dx = adjustedX - centerX;
                    double dy = adjustedY - centerY;
                    adjustedX = dx * Math.Cos(angle) - dy * Math.Sin(angle) + centerX;
                    adjustedY = dx * Math.Sin(angle) + dy * Math.Cos(angle) + centerY;
                }

                if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                    return false;

                var mm = xf.ToMachine(adjustedX, adjustedY);
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
        /// Update live fit status label, including pan offset
        /// </summary>
        private void UpdatePopupLiveFitLabel(double angle)
        {
            try
            {
                if (FitLiveStatusText == null || _gcodeView == null) return;

                // Calculate pan offset in mm
                double panMmX = 0, panMmY = 0;
                double panX = GCodePanTransform?.X ?? 0;
                double panY = GCodePanTransform?.Y ?? 0;
                
                if (Math.Abs(panX) > 0.1 || Math.Abs(panY) > 0.1)
                {
                    // Convert pan pixels to mm using scale factor
                    double canvasWidth = TopViewCanvas?.ActualWidth ?? 0;
                    double canvasHeight = TopViewCanvas?.ActualHeight ?? 0;
                    if (canvasWidth > 0 && canvasHeight > 0 &&
                        CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                    {
                        panMmX = panX / xf.Scale;
                        panMmY = -panY / xf.Scale; // Y inverted
                    }
                }

                var (fits, details) = _gcodeView.CheckLiveFitAtAngle(angle, panMmX, panMmY);
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
                
                // Sync with GCodeView status bar
                _gcodeView.RefreshFitStatus();
            }
            catch { }
        }

        private async void GotoTouchedCoordButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (App.MainController?.IsConnected != true)
                {
                    Log("> ❌ Not connected – cannot move");
                    return;
                }

                // Calculate target from pan offset
                // Pan offset is in pixels - we need to convert to mm
                double panX = GCodePanTransform?.X ?? 0;
                double panY = GCodePanTransform?.Y ?? 0;
                
                if (Math.Abs(panX) < 0.1 && Math.Abs(panY) < 0.1)
                {
                    Log("> ⚠️ No pan offset – nothing to do");
                    return;
                }

                // Get canvas dimensions
                double canvasWidth = TopViewCanvas?.ActualWidth ?? 0;
                double canvasHeight = TopViewCanvas?.ActualHeight ?? 0;
                if (canvasWidth <= 0 || canvasHeight <= 0)
                {
                    Log("> ⚠️ Invalid canvas size");
                    return;
                }

                // Create transform to get the Scale factor (pixels per mm)
                if (!CncControlApp.Helpers.WorkspaceTransform.TryCreateFromSettings(canvasWidth, canvasHeight, out var xf))
                {
                    Log("> ⚠️ Cannot create workspace transform");
                    return;
                }

                // Current machine position
                var mStatus = App.MainController.MStatus;
                double currentMachineX = mStatus?.X ?? 0;
                double currentMachineY = mStatus?.Y ?? 0;
                double currentWorkZ = mStatus?.WorkZ ?? 0;

                // Convert pan offset from pixels to mm using the scale factor
                // Scale = pixels per mm, so mm = pixels / scale
                // 
                // IMPORTANT: In TransformGroup, transforms apply in order: first Rotate, then Translate
                // This means pan (Translate) is applied AFTER rotation, in SCREEN coordinates.
                // However, screen coords != machine coords when there's rotation.
                // 
                // Pan offset (panX, panY) tells us how much the GCode canvas moved on screen.
                // We need to convert this to machine coordinates by REVERSING the rotation.
                // 
                // Canvas Y is inverted: down = positive pixels, but machine Y up = positive mm
                double panScreenMmX = panX / xf.Scale;
                double panScreenMmY = -panY / xf.Scale;  // Y is inverted on canvas
                
                // Now rotate the pan offset from screen coordinates back to machine coordinates
                // If GCode was rotated by angle θ, screen coords are rotated by θ from machine coords
                // To get machine delta, we rotate by -θ
                double angleRad = -_pendingAngle * Math.PI / 180.0;  // Negative to reverse
                double cosA = Math.Cos(angleRad);
                double sinA = Math.Sin(angleRad);
                
                double panMmX = panScreenMmX * cosA - panScreenMmY * sinA;
                double panMmY = panScreenMmX * sinA + panScreenMmY * cosA;

                Log($"> Pending angle: {_pendingAngle:F1}° -> Pan screen({panScreenMmX:F3},{panScreenMmY:F3}) -> machine({panMmX:F3},{panMmY:F3})");

                // Target = current position + pan offset in mm
                double targetMachineX = currentMachineX + panMmX;
                double targetMachineY = currentMachineY + panMmY;

                // Clamp to table limits (settings $130/$131)
                double tableMaxX = xf.MaxX;
                double tableMaxY = xf.MaxY;

                if (targetMachineX < 0) { Log($"> ⚠️ Clamping X {targetMachineX:F3} to 0"); targetMachineX = 0; }
                if (targetMachineY < 0) { Log($"> ⚠️ Clamping Y {targetMachineY:F3} to 0"); targetMachineY = 0; }
                if (targetMachineX > tableMaxX) { Log($"> ⚠️ Clamping X {targetMachineX:F3} to {tableMaxX:F3}"); targetMachineX = tableMaxX; }
                if (targetMachineY > tableMaxY) { Log($"> ⚠️ Clamping Y {targetMachineY:F3} to {tableMaxY:F3}"); targetMachineY = tableMaxY; }

                Log($"> Pan offset: pixels({panX:F1}, {panY:F1}) -> mm({panMmX:F3}, {panMmY:F3})");
                Log($"> Current: X={currentMachineX:F3} Y={currentMachineY:F3} -> Target: X={targetMachineX:F3} Y={targetMachineY:F3}");

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
                    Log("> ❌ Move command failed – aborting");
                    StopMachinePositionUpdateTimer();
                    return;
                }

                // Always wait deterministically for Idle after movement completes
                await WaitForIdleState();

                StopMachinePositionUpdateTimer();

                // Reset pan offset after movement (spindle is now at target position)
                ResetPan();
                
                // Redraw popup canvas BEFORE asking for Zero confirmation
                UiHelper.RunOnUi(() =>
                {
                    if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                    {
                        // Update rotation center to new spindle position BEFORE redraw
                        // This ensures the rotated GCode appears correctly around the new origin
                        if (GCodeRotateTransform != null && Math.Abs(_pendingAngle) > 0.01)
                        {
                            var newOrigin = _gcodeView?.GetGCodeOriginCanvasPosition(TopViewCanvas);
                            if (newOrigin.HasValue)
                            {
                                Log($"> Updating rotation center to new spindle pos: ({newOrigin.Value.X:F1}, {newOrigin.Value.Y:F1})");
                                GCodeRotateTransform.CenterX = newOrigin.Value.X;
                                GCodeRotateTransform.CenterY = newOrigin.Value.Y;
                            }
                        }
                        
                        Log("> Redraw after move");
                        _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                        UpdatePopupLiveFitLabel(_pendingAngle);
                    }
                }, DispatcherPriority.Send);

                // Always show zero confirmation dialog after movement
                if (!_awaitingZeroPrompt)
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

                // Final popup redraw and update button states
                UiHelper.RunOnUi(() =>
                {
                    if (TopViewCanvas != null && TopViewOverlayCanvas != null)
                    {
                        Log("> Final redraw after move");
                        _gcodeView?.RedrawPopupTopView(TopViewCanvas, TopViewOverlayCanvas);
                    }
                    RefreshApplyResetState();
                });
            }
            catch (Exception ex)
            {
                StopMachinePositionUpdateTimer();
                _awaitingZeroPrompt = false; // ensure gate released on unexpected errors
                Log($"> ❌ ERROR during move sequence: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task<bool> ShowZeroConfirmationDialog()
        {
            try
            {
                // Get current machine position for display
                var mStatus = App.MainController?.MStatus;
                double machineX = mStatus?.X ?? 0;
                double machineY = mStatus?.Y ?? 0;
                
                string message = string.Format(CultureInfo.InvariantCulture,
                    "Bu konumu X ve Y sıfır noktası olarak ayarla?\n\nMakine Pozisyonu:\n X: {0:F3} mm\n Y: {1:F3} mm\n\nÇalıştırılacak komut: G10 L20 P0 X0 Y0\n(Kalıcı sıfır EEPROM'a kaydedilir)",
                    machineX, machineY);
                var disp = Application.Current?.Dispatcher;
                if (disp == null || disp.CheckAccess())
                    return MessageDialog.ShowConfirm("Sıfırlama Onayı", message);
                return disp.Invoke(new Func<bool>(() => MessageDialog.ShowConfirm("Sıfırlama Onayı", message)), DispatcherPriority.Send);
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
