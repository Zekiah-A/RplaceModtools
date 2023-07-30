using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;

namespace RplaceModtools.ViewModels;

public partial class SelectionSizeStateInfoViewModel : ObservableObject
{
    [ObservableProperty] private int regionWidth;
    [ObservableProperty] private int regionHeight;
}