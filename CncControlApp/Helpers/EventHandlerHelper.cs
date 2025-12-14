using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CncControlApp.Helpers
{
    /// <summary>
    /// Helper to reduce event handler boilerplate and duplication
    /// </summary>
    public static class EventHandlerHelper
    {
        /// <summary>
        /// Wrap an async event handler with try-catch and logging
        /// </summary>
        public static async Task SafeHandleAsync(Func<Task> handler, string operationName, Action<string> logger = null)
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                var message = $"> ❌ HATA: {operationName} - {ex.Message}";
                logger?.Invoke(message);
                System.Diagnostics.Debug.WriteLine(message);
            }
        }

        /// <summary>
        /// Wrap a synchronous event handler with try-catch and logging
        /// </summary>
        public static void SafeHandle(Action handler, string operationName, Action<string> logger = null)
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                var message = $"> ❌ HATA: {operationName} - {ex.Message}";
                logger?.Invoke(message);
                System.Diagnostics.Debug.WriteLine(message);
            }
        }

        /// <summary>
        /// Create a generic button click handler with automatic error handling
        /// </summary>
        public static void CreateButtonHandler(
            Button button,
            Func<Task> asyncAction,
            string actionName,
            Action<string> logger = null)
        {
            if (button == null) return;

            button.Click += async (s, e) => await SafeHandleAsync(asyncAction, actionName, logger);
        }

        /// <summary>
        /// Create touch event handlers with automatic state management
        /// </summary>
        public static void CreateTouchHandlers(
            Button button,
            Func<TouchEventArgs, Task> touchStart,
            Func<TouchEventArgs, Task> touchEnd,
            string operationName,
            Action<string> logger = null)
        {
            if (button == null) return;

            button.TouchDown += async (s, e) =>
            {
                e.Handled = true;
                try
                {
                    if (button.CaptureTouch(e.TouchDevice))
                    {
                        await touchStart(e);
                    }
                }
                catch (Exception ex)
                {
                    var message = $"> ❌ HATA: {operationName}_TouchStart - {ex.Message}";
                    logger?.Invoke(message);
                }
            };

            button.TouchUp += async (s, e) =>
            {
                e.Handled = true;
                try
                {
                    button.ReleaseTouchCapture(e.TouchDevice);
                    await touchEnd(e);
                }
                catch (Exception ex)
                {
                    var message = $"> ❌ HATA: {operationName}_TouchEnd - {ex.Message}";
                    logger?.Invoke(message);
                }
            };

            button.LostTouchCapture += (s, e) =>
            {
                e.Handled = true;
                SafeHandle(() => button.ReleaseTouchCapture(e.TouchDevice),
                    $"{operationName}_LostTouch", logger);
            };
        }

        /// <summary>
        /// Batch create multiple similar event handlers (e.g., for jog buttons)
        /// </summary>
        public static void CreateJogButtonHandlers(
            Dictionary<string, (Button button, Func<Task> action)> buttonActions,
            Action<string> logger = null)
        {
            foreach (var kvp in buttonActions)
            {
                var buttonName = kvp.Key;
                var (button, action) = kvp.Value;

                if (button == null) continue;

                // Mouse handlers
                button.MouseLeftButtonDown += async (s, e) =>
                {
                    // Filter touch-to-mouse promotion
                    if (e.StylusDevice != null)
                    {
                        e.Handled = true;
                        return;
                    }

                    try
                    {
                        if (button.CaptureMouse())
                        {
                            await action();
                        }
                    }
                    catch (Exception ex)
                    {
                        var message = $"> ❌ HATA: {buttonName}_MouseStart - {ex.Message}";
                        logger?.Invoke(message);
                    }
                };

                button.MouseLeftButtonUp += (s, e) =>
                {
                    button.ReleaseMouseCapture();
                    e.Handled = true;
                };

                button.MouseLeave += (s, e) =>
                {
                    if (button.IsMouseCaptured)
                    {
                        button.ReleaseMouseCapture();
                    }
                };

                // Touch handlers
                CreateTouchHandlers(
                    button,
                    e => action(),
                    e => Task.CompletedTask,
                    buttonName,
                    logger
                );
            }
        }

        /// <summary>
        /// Filter touch-to-mouse event promotion
        /// </summary>
        public static bool IsTouchPromotedToMouse(MouseEventArgs e)
        {
            return e.StylusDevice != null;
        }

        /// <summary>
        /// Check if event should be handled based on touch state
        /// </summary>
        public static bool ShouldHandleMouseEvent(MouseEventArgs e, ref bool touchEventInProgress)
        {
            if (touchEventInProgress)
            {
                return false;
            }

            if (IsTouchPromotedToMouse(e))
            {
                return false;
            }

            return true;
        }
    }
}
