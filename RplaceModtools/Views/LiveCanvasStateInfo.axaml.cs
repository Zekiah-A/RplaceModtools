using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.ViewModels;

namespace RplaceModtools.Views;

public partial class CanvasStateInfo : UserControl
{
    private LiveCanvasStateInfoViewModel? viewModel;

    public CanvasStateInfo()
    {
        viewModel = App.Current.Services.GetRequiredService<LiveCanvasStateInfoViewModel>();
        DataContext = viewModel;
        InitializeComponent();
    }
}