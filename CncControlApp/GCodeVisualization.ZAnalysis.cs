using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CncControlApp.Helpers;

namespace CncControlApp
{
    /// <summary>
    /// Z-level analysis and UI update methods
    /// </summary>
    public partial class GCodeVisualization
    {
        /// <summary>
        /// Analyze Z levels and update UI
        /// </summary>
        public void AnalyzeZLevels(List<GCodeSegment> gcodeSegments)
        {
            if (gcodeSegments == null || gcodeSegments.Count == 0)
            {
                UpdateZInfoInUI(0, 0, 0, 0);
                return;
            }

            var allZValues = new List<double>();
            foreach (var segment in gcodeSegments)
            {
                allZValues.Add(segment.StartPoint.Z);
                allZValues.Add(segment.EndPoint.Z);
            }

            double minZ = allZValues.Min();
            double maxZ = allZValues.Max();
            double zRange = maxZ - minZ;
            double totalZHeight = Math.Abs(zRange);

            var uniqueZLevels = allZValues
                .Select(z => Math.Round(z, 3))
                .Distinct()
                .OrderBy(z => z)
                .ToList();

            UpdateZInfoInUI(uniqueZLevels.Count, totalZHeight, minZ, maxZ);
        }

        /// <summary>
        /// Update Z information in UI
        /// </summary>
        private void UpdateZInfoInUI(int layerCount, double totalHeight, double minZ, double maxZ)
        {
            try
            {
                UiHelper.RunOnUi(() =>
                {
                    var mainWindow = Application.Current.MainWindow;

                    var zLayersTextBlock = FindControlByName<TextBlock>(mainWindow, "ZLayersTextBlock");
                    if (zLayersTextBlock != null)
                    {
                        zLayersTextBlock.Text = layerCount.ToString();
                    }

                    var zHeightTextBlock = FindControlByName<TextBlock>(mainWindow, "ZHeightTextBlock");
                    if (zHeightTextBlock != null)
                    {
                        zHeightTextBlock.Text = $"{totalHeight:F3}mm";
                    }

                    var zRangeTextBlock = FindControlByName<TextBlock>(mainWindow, "ZRangeTextBlock");
                    if (zRangeTextBlock != null)
                    {
                        zRangeTextBlock.Text = $"{minZ:F3} ↔ {maxZ:F3}";
                    }
                }, System.Windows.Threading.DispatcherPriority.Send);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UI Update Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Find UI control by name
        /// </summary>
        private T FindControlByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T control && control.Name == name)
                    return control;

                var result = FindControlByName<T>(child, name);
                if (result != null) return result;
            }

            return null;
        }
    }
}
