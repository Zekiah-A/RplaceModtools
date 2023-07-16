using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;

public partial class LiveChatChannelViewModel : ObservableObject
{
    [ObservableProperty] private string channelName;
    [ObservableProperty] private ObservableCollection<ChatMessage> messages;

    public LiveChatChannelViewModel(string channel)
    {
        channelName = channel;
        messages = new ObservableCollection<ChatMessage>();
    }

    public void AddMessage(ChatMessage message)
    {
        messages.Add(message);
    }
}