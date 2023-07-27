using System.Globalization;
using Avalonia.Data.Converters;

namespace RplaceModtools;

public class AllTrueConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return values.All(value => value is true);
    }
}