using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CncControlApp
{
    public class StepBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (value is bool isChecked && isChecked)
                {
                    return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green color #4CAF50
                }
                
                return new SolidColorBrush(Color.FromRgb(66, 66, 66)); // Default dark gray #424242
            }
            catch
            {
                return new SolidColorBrush(Color.FromRgb(66, 66, 66));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}