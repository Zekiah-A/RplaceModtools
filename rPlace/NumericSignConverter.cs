using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace rPlace;

public class NumericSignConverter : IValueConverter
{
    public static readonly NumericSignConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double number || !targetType.IsAssignableTo(typeof(double)))
            return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);

        if ((double?) number > 0)
            return (double?) number * -1;
        
        return Math.Abs(number);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
