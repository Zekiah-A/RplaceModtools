using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.Models;

public partial class ServerPreset : ObservableObject
{
    public static readonly ServerPreset Default = new();

    [ObservableProperty] private bool legacyServer = true;
    [ObservableProperty] private string websocket = "wss://server.rplace.tk:443";
    [ObservableProperty] private string fileServer = "https://raw.githubusercontent.com/rplacetk/canvas1/";
    [ObservableProperty] private string adminKey = "";
    [ObservableProperty] private string placePath = "/place";
    
    // Legacy server only
    [ObservableProperty] private string backupsRepository = "https://github.com/rplacetk/canvas1";
    [ObservableProperty] private string mainBranch = "main";

    // New server only
    [ObservableProperty] private string backupListPath = "/backuplist";
    [ObservableProperty] private string backupsPath = "/backups/";
    
    // Mutable, will not create a new preset if changed
    [ObservableProperty] private string? chatUsername = null;
}