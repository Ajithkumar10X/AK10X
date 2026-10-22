using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WindowsApp;

public class PositiveToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intVal)
        {
            return intVal > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public static class Converters
{
    public static readonly IValueConverter PositiveToVisible = new PositiveToVisibilityConverter();
}
