using System.Globalization;

namespace TheWell.MAUI.Converters;

/// <summary>
/// Maps IsCompleted → teal (active) or grey (inactive) for the "Yes" button.
/// </summary>
public class BoolToButtonColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Color.FromArgb("#1A6B8A") : Color.FromArgb("#CCCCCC");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

/// <summary>
/// Inverse: maps IsCompleted → grey (inactive) or red (active) for the "No" button.
/// </summary>
public class InvertBoolToButtonColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Color.FromArgb("#CCCCCC") : Color.FromArgb("#D9534F");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
