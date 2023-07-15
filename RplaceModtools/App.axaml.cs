using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.Views;
using RplaceModtools.ViewModels;


namespace RplaceModtools
{
    public partial class App : Application
    {
        public new static App Current => (App) Application.Current!;
        public IServiceProvider Services { get; }

        public App()
        {
            Services = new ServiceCollection()
                .AddSingleton<MainWindow>()
                .AddSingleton<MainWindowViewModel>()
                .AddTransient<LiveCanvasStateInfoViewModel>()
                .AddTransient<PaintBrushStateInfoViewModel>()
                .BuildServiceProvider();
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = Current.Services.GetRequiredService<MainWindow>();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
