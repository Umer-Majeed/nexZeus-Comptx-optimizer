using System;
using System.Globalization;
using System.Windows.Data;

namespace NexZeus
{
    public class ExpandArrowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isExpanded = value is bool b && b;
            return isExpanded ? "▲" : "▼";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}