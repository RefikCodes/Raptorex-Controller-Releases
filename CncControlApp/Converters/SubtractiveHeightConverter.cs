using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace CncControlApp.Converters
{
    /// <summary>
    /// values[0] = Panel (Border) ActualHeight
    /// values[1] = Üst label ActualHeight
    /// values[2] = Alt label ActualHeight
    /// ConverterParameter: ekstra sabit çıkarmalar (padding + label alt/üst margin + opsiyonel boşluk)
    /// Örn: "54" => 54 px daha çıkar.
    /// </summary>
    public class SubtractiveHeightConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double panel = Get(values, 0);
            double top   = Get(values, 1);
            double bottom= Get(values, 2);

            double extra = 0;
            if (parameter != null && double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                extra = p;

            double h = panel - top - bottom - extra;
            if (h < 0) h = 0;
            return h;
        }

        private double Get(object[] arr, int idx)
        {
            if (idx >= arr.Length) return 0;
            if (arr[idx] is double d && !double.IsNaN(d)) return d;
            return 0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => targetTypes.Select(t => Binding.DoNothing).ToArray();
    }
}