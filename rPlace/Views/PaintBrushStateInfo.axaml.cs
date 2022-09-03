using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace rPlace.Views;

public partial class PaintBrushStateInfo : UserControl
{
    public PaintBrushStateInfo()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}