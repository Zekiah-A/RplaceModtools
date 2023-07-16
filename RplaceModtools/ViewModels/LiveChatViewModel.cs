using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;

public partial class LiveChatViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<LiveChatChannelViewModel> channels;
    [ObservableProperty] private LiveChatChannelViewModel currentChannel;

    public LiveChatViewModel()
    {
        var englishDefault = new LiveChatChannelViewModel("en");
        var turkishDefault = new LiveChatChannelViewModel("tr");
        channels = new ObservableCollection<LiveChatChannelViewModel>()
        {
            englishDefault,
            turkishDefault
        };

        currentChannel = englishDefault;
    }
}
