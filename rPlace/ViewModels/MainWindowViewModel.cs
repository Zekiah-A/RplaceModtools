using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using rPlace.Models;
using rPlace.Views;

namespace rPlace.ViewModels;
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private ServerPreset currentPreset = new();
    [ObservableProperty] private int currentPaintBrushRadius = 1;
    [ObservableProperty] private Shape currentBrushShape = Shape.Square;
    [ObservableProperty] private ObservableObject? stateInfo;
    [ObservableProperty] private Tool? currentTool;
    public string[] KnownWebsockets => new[] {"wss://server2.rplace.tk:443/", "wss://server.rplace.tk:443/"};
    public string[] KnownFileServers => new[] {"https://server2.rplace.tk:8081/", "https://github.com/rslashplace2/rslashplace2.github.io/raw/main/"};

    [RelayCommand]
    private void SelectPaintTool() => CurrentTool = Tool.PaintBrush;

    [RelayCommand]
    private void SelectRubberTool() => CurrentTool = Tool.Rubber;
    
    [RelayCommand]
    private void SelectSelectionTool() => CurrentTool = Tool.Select;
}