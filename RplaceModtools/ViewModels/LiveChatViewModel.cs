using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;

public partial class LiveChatViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<ChatMessage> messages;

    public LiveChatViewModel()
    {
        messages = new ObservableCollection<ChatMessage>();
    }

    public void AddMessage(ChatMessage message)
    {
        messages.Add(message);
    }
}
