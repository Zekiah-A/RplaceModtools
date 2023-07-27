using System.Globalization;

namespace RplaceModtools;

using System;
using Avalonia.Data.Converters;

public class ValueEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value?.Equals(parameter) == true;
    }

    /// <summary>
    /// Will convert the boolean it normally controls back into setting the value of the control to this thing's source param
    /// </summary>
    /// <param name="value">Value of the property in the control that is bound (bool)</param>
    /// <param name="parameter">ConverterParameter</param>
    /// <returns>What source type should be</returns>
    /// <exception cref="NotSupportedException"></exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is null)
        {
            return null;
        }
        if (value is not bool booleanValue)
        {
            throw new NotSupportedException("Value of control bound property is not boolean");
        }
        if (parameter.GetType() != targetType)
        {
            throw new NotSupportedException("Type of converter parameter not of same type as data this property is bound to");
        }

        return booleanValue ? parameter : value;
    }
}