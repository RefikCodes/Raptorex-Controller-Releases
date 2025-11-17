using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CncControlApp
{
    public partial class MainWindow
    {
        private void DisableUIElementsExceptTextBoxes(DependencyObject parent)
        {
            try
            {
                if (parent == null) return;
                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is TextBox)
                    {
                        // allow text input
                    }
                    else if (child is Control c)
                    {
                        c.IsEnabled = false;
                    }

                    DisableUIElementsExceptTextBoxes(child);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disable elements error: {ex.Message}");
            }
        }

        private void EnableAllUIElements(DependencyObject parent)
        {
            try
            {
                if (parent == null) return;
                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is Control c)
                    {
                        c.IsEnabled = true;
                    }

                    EnableAllUIElements(child);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Enable elements error: {ex.Message}");
            }
        }
    }
}