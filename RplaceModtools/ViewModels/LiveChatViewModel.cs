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
        var englishChannel = new LiveChatChannelViewModel("en", "English");

        channels = new ObservableCollection<LiveChatChannelViewModel>()
        {
            englishChannel,
            new LiveChatChannelViewModel("tr", "Turkish"),
            new LiveChatChannelViewModel("zh", "Chinese"),
            new LiveChatChannelViewModel("hi", "Hindi"),
            new LiveChatChannelViewModel("es", "Spanish"),
            new LiveChatChannelViewModel("fr", "French"),
            new LiveChatChannelViewModel("ar", "Arabic"),
            new LiveChatChannelViewModel("bn", "Bangla"),
            new LiveChatChannelViewModel("ru", "Russian"),
            new LiveChatChannelViewModel("pt", "Portugese"),
            new LiveChatChannelViewModel("ur", "Urdu"),
            new LiveChatChannelViewModel("de", "German"),
            new LiveChatChannelViewModel("jp", "Japanese"),
            new LiveChatChannelViewModel("vi", "Vietnamese"),
            new LiveChatChannelViewModel("ko", "Korean"),
            new LiveChatChannelViewModel("it", "Italian"),
            new LiveChatChannelViewModel("fa", "Farsi"),
            new LiveChatChannelViewModel("sr", "Serbian"),
            new LiveChatChannelViewModel("az", "Azerbaijani"),
        };
        currentChannel = englishChannel;
    }
}
