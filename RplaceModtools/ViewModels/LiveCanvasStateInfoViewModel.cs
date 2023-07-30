using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.ViewModels;

public partial class LiveCanvasStateInfoViewModel : ObservableObject, ITransientStateInfo
{
    public TimeSpan PersistsFor { get; set; } = TimeSpan.FromSeconds(10);
    public DateTime SpawnedOn { get; set; }

    public LiveCanvasStateInfoViewModel()
    {
        SpawnedOn = DateTime.Now;
    }
}