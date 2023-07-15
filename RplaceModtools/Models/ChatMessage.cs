using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.Models;

public class ChatMessage
{
    public string Name { get; set; }
    public string Message { get; set; }
    public string Channel { get; set; }
    public string Type;
    public int X;
    public int Y;
    public string? Uid { get; set; }
}