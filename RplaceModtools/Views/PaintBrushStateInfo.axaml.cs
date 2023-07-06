using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.ViewModels;

namespace RplaceModtools.Views;

public partial class PaintBrushStateInfo : UserControl
{
    private PaintBrushStateInfoViewModel viewModel;
    
    public PaintBrushStateInfo()
    {
        viewModel = App.Current.Services.GetRequiredService<PaintBrushStateInfoViewModel>();
        DataContext = viewModel;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}