using System;
using System.Globalization;
using System.Windows.Data;

namespace NexZeus
{
    public class SizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
                return TempCleaner.FormatSize(bytes);
            return "0 MB";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}