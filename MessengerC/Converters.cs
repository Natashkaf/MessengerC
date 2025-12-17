

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MessengerApp.Converters
{
    public class FileTypeToIconConverter : IValueConverter
    {
        private IValueConverter _valueConverterImplementation;

        object? IValueConverter.ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return _valueConverterImplementation.ConvertBack(value, targetType, parameter, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return _valueConverterImplementation.Convert(value, targetType, parameter, culture);
        }
    }

    public class MessageStatusToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var status = value as string;
            return status switch
            {
                "sent" => "✓",
                "delivered" => "✓✓",
                "read" => "✓✓",
                "error" => "",
                _ => ""
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsPhotoToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPhoto)
            {
                return isPhoto ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                string[] sizes = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
                int order = 0;
                double len = bytes;

                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len = len / 1024;
                }

                return $"{len:0.##} {sizes[order]}";
            }
            return "0 Б";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}