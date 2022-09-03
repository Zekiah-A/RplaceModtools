using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using rPlace.ViewModels;

namespace rPlace.Views;

public partial class LiveCanvasStateInfo : UserControl
{
    public LiveCanvasStateInfo()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}