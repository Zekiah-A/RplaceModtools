using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.Views;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private ServerPreset currentPreset = new();
    [ObservableProperty] private int currentPaintBrushRadius = 1;
    [ObservableProperty] private Shape currentBrushShape = Shape.Square;
    [ObservableProperty] private ObservableObject? stateInfo;
    [ObservableProperty] private Tool? currentTool;
    public string[] KnownWebsockets => new[] {"wss://server.poemanthology.org/ws", "wss://server.rplace.tk:443"};
    public string[] KnownFileServers => new[] {"https://server.poemanthology.org/", "https://raw.githubusercontent.com/rplacetk/canvas1/main/"};

    [RelayCommand]
    private void SelectPaintTool() => CurrentTool = Tool.PaintBrush;

    [RelayCommand]
    private void SelectRubberTool() => CurrentTool = Tool.Rubber;
    
    [RelayCommand]
    private void SelectSelectionTool() => CurrentTool = Tool.Select;
}