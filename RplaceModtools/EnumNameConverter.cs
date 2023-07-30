namespace RplaceModtools;

using System;
using Avalonia.Data.Converters;
using System.Globalization;

public class EnumNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return enumValue.ToString();
        }

        throw new ArgumentException("Binding property value was not of enum type");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue && targetType.IsEnum)
        {
            return Enum.Parse(targetType, stringValue);
        }

        throw new ArgumentException("Control property value was not of string type for back conversion");
    }
}