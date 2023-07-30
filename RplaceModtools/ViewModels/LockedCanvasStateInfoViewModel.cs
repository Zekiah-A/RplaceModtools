using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.ViewModels;

public class LockedCanvasStateInfoViewModel : ObservableObject, ITransientStateInfo
{
    public TimeSpan PersistsFor { get; set; } = TimeSpan.FromSeconds(10);
    public DateTime SpawnedOn { get; set; }

    public LockedCanvasStateInfoViewModel()
    {
        SpawnedOn = DateTime.Now;
    }
}