using System.Diagnostics;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using rPlace.Models;

namespace rPlace;

public class ShapeEmojiConverter : IValueConverter
{
    public static readonly ShapeEmojiConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        //if (value is not Shape curShape || targetType.IsAssignableTo(typeof(Shape))) return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        return value /*curShape*/ switch
        {
            Shape.Square => "□",
            Shape.Circular => "◯",
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}