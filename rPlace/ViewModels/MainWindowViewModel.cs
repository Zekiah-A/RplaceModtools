using CommunityToolkit.Mvvm.ComponentModel;
using rPlace.Models;

namespace rPlace.ViewModels;
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private ServerPreset currentPreset = new();
    
    public string[] KnownWebsockets => new[] {"wss://server2.rplace.tk:443/", "wss://server.rplace.tk:443/"};
    public string[] KnownFileServers => new[] {"https://server2.rplace.tk:8081/", "https://github.com/rslashplace2/rslashplace2.github.io/raw/main/"};
}