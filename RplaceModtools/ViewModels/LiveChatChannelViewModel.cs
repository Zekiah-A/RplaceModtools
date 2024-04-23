using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;

public partial class LiveChatChannelViewModel : ObservableObject
{
    [ObservableProperty] private string channelName;
    [ObservableProperty] private ObservableCollection<LiveChatMessage> messages;

    public string ChannelCode { get => channelCode; }
    private string channelCode;

    public LiveChatChannelViewModel(string channelCode, string? channelName = null)
    {
        this.channelCode = channelCode;
        this.channelName = channelName ?? channelCode;
        messages = new ObservableCollection<LiveChatMessage>();
    }
}