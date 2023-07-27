using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.Models;

public partial class ServerPresetViewModel : ObservableObject
{
    [ObservableProperty] private bool legacyServer = true;
    [ObservableProperty] private string? websocket;
    [ObservableProperty] private string? fileServer;
    [ObservableProperty] private string? adminKey;
    [ObservableProperty] private string placePath = "/place";
    
    // Legacy server only
    [ObservableProperty] private string backupsRepository = "https://github.com/rplacetk/canvas1.git";

    // New server only
    [ObservableProperty] private string backupListPath = "/backuplist";
    [ObservableProperty] private string backupsPath = "/backups/";
}