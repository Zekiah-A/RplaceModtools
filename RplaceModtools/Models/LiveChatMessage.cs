using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.Models;

public class LiveChatMessage
{
    public uint MessageId { get; set; }
    public uint SenderIntId { get; set; }
    public string Name { get; set; }
    public string Message { get; set; }
    public LiveChatMessage? RepliesTo { get; set; }
    public DateTimeOffset SendDate { get; set; }
}