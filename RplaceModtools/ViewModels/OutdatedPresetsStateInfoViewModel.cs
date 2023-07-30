using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.ViewModels;

public partial class OutdatedPresetsStateInfoViewModel : ObservableObject, ITransientStateInfo
{
    public TimeSpan PersistsFor { get; set; } = TimeSpan.FromSeconds(8);
    public DateTime SpawnedOn { get; set; }

    public OutdatedPresetsStateInfoViewModel()
    {
        SpawnedOn = DateTime.Now;
    }
}