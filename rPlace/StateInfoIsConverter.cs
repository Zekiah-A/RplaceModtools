using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;

namespace rPlace;

public class StateInfoIsConverter : IValueConverter
{
    public static readonly StateInfoIsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ObservableObject stateInfo && parameter is string stateInfoQuery)
            return Type.GetType(stateInfoQuery) == stateInfo.GetType();
        
        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}