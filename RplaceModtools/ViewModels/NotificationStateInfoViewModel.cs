using CommunityToolkit.Mvvm.ComponentModel;

namespace RplaceModtools.ViewModels;

public partial class NotificationStateInfoViewModel : ObservableObject, ITransientStateInfo
{
    [ObservableProperty] private string notification = "";
    
    public TimeSpan PersistsFor { get; set; }
    public DateTime SpawnedOn { get; set; }

    public NotificationStateInfoViewModel(TimeSpan persists)
    {
        PersistsFor = persists;
        SpawnedOn = DateTime.Now;
    }
    
    public NotificationStateInfoViewModel()
    {
        PersistsFor = TimeSpan.FromSeconds(10);
        SpawnedOn = DateTime.Now;
    }

    public void ResetPersistsTo(TimeSpan persists)
    {
        SpawnedOn = DateTime.Now;
        PersistsFor = persists;
    }
}