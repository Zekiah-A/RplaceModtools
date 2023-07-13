using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RplaceModtools;

public class HashColourConverter : IValueConverter
{
    public static readonly HashColourConverter Instance = new();

    private readonly IBrush[] colours =
    {
        Brushes.LightBlue, Brushes.Navy, Brushes.Green, Brushes.MediumPurple, Brushes.Gray, Brushes.Brown, Brushes.OrangeRed, Brushes.Gold
    };

    private static Random random = new();
    
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || !targetType.IsAssignableTo(typeof(IBrush)))
        {
            return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
        }

        var hash = text.Aggregate(0, (current, @char) => (int) ((current * 31 + @char) & 0xFFFFFFFF));
        return colours.ElementAtOrDefault(hash & 7) ?? colours[(int)Math.Floor((double)random.Next() * colours.Length)];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}