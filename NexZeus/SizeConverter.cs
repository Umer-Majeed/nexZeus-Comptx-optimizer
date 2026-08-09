using System;
using System.Globalization;
using System.Windows.Data;

namespace NexZeus
{
    /// <summary>Converts a byte count (long) into a human-readable size string, e.g. "482.3 MB".</summary>
    public class SizeConverter : IValueConverter
    {
        private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not (long or int or double)) return "0 B";

            double size = System.Convert.ToDouble(value);
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < Units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.#} {Units[unitIndex]}";
        }

        public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("SizeConverter is one-way only.");
        }
    }
}