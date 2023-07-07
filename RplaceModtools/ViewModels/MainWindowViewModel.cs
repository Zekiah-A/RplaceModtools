using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private ServerPreset currentPreset = new()
    {
        PlacePath = "/place",
        BackupListPath = "/backuplist",
        BackupsPath = "/backups"
    };
    [ObservableProperty] private int currentPaintBrushRadius = 1;
    [ObservableProperty] private Shape currentBrushShape = Shape.Square;
    [ObservableProperty] private ObservableObject? stateInfo;
    [ObservableProperty] private Tool? currentTool;
    public string[] KnownWebsockets => new[]
    {
        "wss://server.poemanthology.org/ws",
        "wss://server.rplace.tk:443",
        "wss://archive.rplace.tk:444"
    };
    public string[] KnownFileServers => new[]
    {
        "https://server.poemanthology.org/",
        "https://raw.githubusercontent.com/rplacetk/canvas1/main/",
        "https://raw.githubusercontent.com/rplacetk/canvas1/04072023/"
    };

    [RelayCommand]
    private void SelectPaintTool() => CurrentTool = Tool.PaintBrush;

    [RelayCommand]
    private void SelectRubberTool() => CurrentTool = Tool.Rubber;
    
    [RelayCommand]
    private void SelectSelectionTool() => CurrentTool = Tool.Select;
}