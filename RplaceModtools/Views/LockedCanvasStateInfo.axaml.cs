using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RplaceModtools.Views;

public partial class LockedCanvasStateInfo : UserControl
{
    public LockedCanvasStateInfo()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}