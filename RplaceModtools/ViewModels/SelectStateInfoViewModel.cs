using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace RplaceModtools.ViewModels;

public partial class SelectStateInfoViewModel : ObservableObject
{
    [ObservableProperty] private int instanceX;
    [ObservableProperty] private int instanceY;
    [ObservableProperty] private int instanceWidth;
    [ObservableProperty] private int instanceHeight;
    private MainWindowViewModel mainWindowVm;
    
    public SelectStateInfoViewModel()
    {
        mainWindowVm = App.Current.Services.GetRequiredService<MainWindowViewModel>();
    }

    [RelayCommand]
    public void ClearSelections()
    {
        mainWindowVm.ClearSelections();
        mainWindowVm.CurrentSelection = null;
    }

    [RelayCommand]
    public void CyclePrimary()
    {
        if (mainWindowVm.Selections.Count == 0)
        {
            return;
        }
        
        var newIndex = mainWindowVm.CurrentSelection == null ? 0
            : mainWindowVm.Selections.IndexOf(mainWindowVm.CurrentSelection);

        if (newIndex > mainWindowVm.Selections.Count - 1)
        {
            newIndex = 0;
        }

        mainWindowVm.CurrentSelection = mainWindowVm.Selections[newIndex];
    }

    [RelayCommand]
    public void CreateNew()
    {
        mainWindowVm.StartSelection(new Point(InstanceX, InstanceY),
            new Point(InstanceX + InstanceWidth, InstanceY + InstanceHeight));
    }

    [RelayCommand]
    public void MoveCurrentTo()
    {
        mainWindowVm.UpdateSelection(mainWindowVm.CurrentSelection, new Point(InstanceX, InstanceY),
            new Point(InstanceX + InstanceWidth, InstanceY + InstanceHeight));
    }
}