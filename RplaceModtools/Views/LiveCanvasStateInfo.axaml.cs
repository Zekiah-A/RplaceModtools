using Avalonia.Controls;
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