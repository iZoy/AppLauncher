using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AppLauncher.Services;

public class NullToVisibilityConverter : IValueConverter
{
    public bool CollapseWhenNull { get; set; } = true;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNullOrEmpty = value == null;
        if (value is string str) isNullOrEmpty = string.IsNullOrWhiteSpace(str);

        if (CollapseWhenNull)
        {
            return isNullOrEmpty ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            return isNullOrEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
