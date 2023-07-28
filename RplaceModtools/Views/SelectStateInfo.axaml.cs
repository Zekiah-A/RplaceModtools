using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RplaceModtools.Views;

public partial class SelectStateInfo : UserControl
{
    public SelectStateInfo()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}