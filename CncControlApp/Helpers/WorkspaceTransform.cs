using System;
using System.Globalization;
using System.Linq;
using System.Windows;

namespace CncControlApp.Helpers
{
    /// <summary>
    /// Single source of truth for mapping between machine coordinates (mm) and canvas pixels.
    /// Uses table limits ($130/$131), canvas size, and a margin factor to compute scale and centers.
    /// </summary>
    public class WorkspaceTransform
    {
        public double CanvasWidth { get; }
        public double CanvasHeight { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Scale { get; }
        public double CanvasCenterX { get; }
        public double CanvasCenterY { get; }
        public double TableCenterX { get; }
        public double TableCenterY { get; }

        public WorkspaceTransform(double canvasWidth, double canvasHeight, double maxX, double maxY, double marginFactor = 0.9)
        {
            if (canvasWidth <= 0 || canvasHeight <= 0) throw new ArgumentException("Invalid canvas size");
            if (maxX <= 0 || maxY <= 0) throw new ArgumentException("Invalid table limits");
            if (marginFactor <= 0 || marginFactor > 1) marginFactor = 0.9;

            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            MaxX = maxX;
            MaxY = maxY;

            double sX = canvasWidth / maxX;
            double sY = canvasHeight / maxY;
            Scale = Math.Min(sX, sY) * marginFactor;

            CanvasCenterX = canvasWidth / 2.0;
            CanvasCenterY = canvasHeight / 2.0;
            TableCenterX = maxX / 2.0;
            TableCenterY = maxY / 2.0;
        }

        /// <summary>
        /// Factory that reads $130/$131 from App.MainController.Settings.
        /// </summary>
        public static bool TryCreateFromSettings(double canvasWidth, double canvasHeight, out WorkspaceTransform transform, double marginFactor = 0.9)
        {
            transform = null;
            try
            {
                if (App.MainController?.Settings == null || App.MainController.Settings.Count == 0) return false;
                var xLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 130);
                var yLimit = App.MainController.Settings.FirstOrDefault(s => s.Id == 131);
                if (xLimit == null || yLimit == null) return false;

                if (!double.TryParse(xLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxX)) return false;
                if (!double.TryParse(yLimit.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double maxY)) return false;
                if (maxX <= 0 || maxY <= 0) return false;

                transform = new WorkspaceTransform(canvasWidth, canvasHeight, maxX, maxY, marginFactor);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Point ToCanvas(double machineX, double machineY)
        {
            double cx = (machineX - TableCenterX) * Scale + CanvasCenterX;
            double cy = CanvasCenterY - (machineY - TableCenterY) * Scale; // Y inverted
            return new Point(cx, cy);
        }

        public (double X, double Y) ToMachine(double canvasX, double canvasY)
        {
            double x = (canvasX - CanvasCenterX) / Scale + TableCenterX;
            double y = (CanvasCenterY - canvasY) / Scale + TableCenterY;
            return (x, y);
        }

        /// <summary>
        /// Returns the four corners of the table rectangle on canvas using this transform.
        /// </summary>
        public (Point TL, Point TR, Point BR, Point BL) GetTableCornersOnCanvas()
        {
            double left = CanvasCenterX - (MaxX / 2.0) * Scale;
            double right = CanvasCenterX + (MaxX / 2.0) * Scale;
            double top = CanvasCenterY - (MaxY / 2.0) * Scale;
            double bottom = CanvasCenterY + (MaxY / 2.0) * Scale;
            var TL = new Point(left, top);
            var TR = new Point(right, top);
            var BR = new Point(right, bottom);
            var BL = new Point(left, bottom);
            return (TL, TR, BR, BL);
        }
    }
}
