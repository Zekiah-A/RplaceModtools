using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using rPlace.ViewModels;

namespace rPlace.Views;

public partial class LiveCanvasStateInfo : UserControl
{
    private LiveCanvasStateInfoViewModel? viewModel;
    
    public LiveCanvasStateInfo()
    {
        InitializeComponent();
    }
    
    
    private void InitializeComponent()
    {
        viewModel = App.Current.Services.GetRequiredService<LiveCanvasStateInfoViewModel>();
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
    }
}