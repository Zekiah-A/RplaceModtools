using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using RplaceModtools.ViewModels;

namespace RplaceModtools.Views;

public partial class NotificationStateInfo : UserControl
{
    public NotificationStateInfo()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}