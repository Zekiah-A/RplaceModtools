using Avalonia.Markup.Xaml;

namespace RplaceModtools;

public class EnumBindingSourceExtension : MarkupExtension
{
    private Type enumType;

    public EnumBindingSourceExtension(Type type)
    {
        if (type is not { IsEnum: true })
        {
            throw new ArgumentException("Enum type is required.", nameof(type));
        }

        enumType = type;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Enum.GetValues(enumType);
    }
}