using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using RplaceModtools.Models;

namespace RplaceModtools;

public class CurrentToolIsConverter : IValueConverter
{
    public static readonly CurrentToolIsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Tool tool || parameter is not string toolQuery)
        {
            return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        }
        
        return tool == Enum.Parse<Tool>(toolQuery);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}