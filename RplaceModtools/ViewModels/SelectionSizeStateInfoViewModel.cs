using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;

namespace RplaceModtools.ViewModels;

public partial class SelectionSizeStateInfoViewModel : ObservableObject, ITransientStateInfo
{
    public TimeSpan PersistsFor { get; set; } = TimeSpan.FromSeconds(5);
    public DateTime SpawnedOn { get; set; }

    [ObservableProperty] private int regionWidth;
    [ObservableProperty] private int regionHeight;
    

    public SelectionSizeStateInfoViewModel()
    {
        SpawnedOn = DateTime.Now;
    }
}