using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;

public partial class LiveChatChannelViewModel : ObservableObject
{
    [ObservableProperty] private string channelName;
    [ObservableProperty] private ObservableCollection<LiveChatMessage> messages;

    public LiveChatChannelViewModel(string channel)
    {
        channelName = channel;
        messages = new ObservableCollection<LiveChatMessage>();
    }
}