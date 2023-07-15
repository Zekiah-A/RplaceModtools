using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.ViewModels;

namespace RplaceModtools.Views;

public partial class LiveCanvasStateInfo : UserControl
{
    private LiveCanvasStateInfoViewModel? viewModel;
    
    public LiveCanvasStateInfo()
    {
        viewModel = App.Current.Services.GetRequiredService<LiveCanvasStateInfoViewModel>();
        DataContext = viewModel;
        InitializeComponent();
    }
}