using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.ViewModels;

public partial class OutdatedPresetsStateInfoViewModel : ObservableObject
{
    [ObservableProperty] private string oldPresetsPath;
}