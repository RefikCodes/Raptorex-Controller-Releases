using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CncControlApp.Helpers
{
    public static class TouchEventHelper
    {
        public static Point GetRelativePosition(TouchEventArgs e, FrameworkElement element, Orientation orientation)
        {
            Point touchPoint = e.GetTouchPoint(element).Position;
            
            double relativePosition;
            if (orientation == Orientation.Vertical)
            {
                relativePosition = 1.0 - (touchPoint.Y / element.ActualHeight);
            }
            else
            {
                relativePosition = touchPoint.X / element.ActualWidth;
            }
            
            relativePosition = Math.Max(0, Math.Min(1, relativePosition));
            return new Point(relativePosition, 0);
        }

        public static double CalculateSliderValue(Slider slider, double relativePosition)
        {
            return slider.Minimum + (slider.Maximum - slider.Minimum) * relativePosition;
        }
    }
}