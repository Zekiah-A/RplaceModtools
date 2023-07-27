using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.Models;
using RplaceModtools.Views;

namespace RplaceModtools.ViewModels;
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private bool currentModerationAll = false;
    [ObservableProperty] private ModerationAction currentModerationAction = ModerationAction.None;
    [ObservableProperty] private TimeSpan currentModerationDuration;
    [ObservableProperty] private string currentModerationReason;
    [ObservableProperty] private string currentModerationUid;

    [ObservableProperty] private ServerPresetViewModel currentPreset = new();
    [ObservableProperty] private int currentPaintBrushRadius = 1;
    [ObservableProperty] private Shape currentBrushShape = Shape.Square;
    [ObservableProperty] private ObservableObject? stateInfo;
    [ObservableProperty] private Tool? currentTool;

    [ObservableProperty] private ObservableCollection<string> backups = new();
    public string[] KnownWebsockets => new[]
    {
        "wss://server.poemanthology.org/ws",
        "wss://server.rplace.tk:443",
        "wss://archive.rplace.tk:444"
    };
    public string[] KnownFileServers => new[]
    {
        "https://server.poemanthology.org/",
        "https://raw.githubusercontent.com/rplacetk/canvas1/main/",
        "https://raw.githubusercontent.com/rplacetk/canvas1/04072023/"
    };
    public string? ChatUsername { get; set; }

    public static readonly string ProgramDirectory =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RplaceModtools");
    private static readonly string presetPath =
        Path.Join(ProgramDirectory, "server_presets.txt");
    
    public ObservableCollection<ServerPresetViewModel> ServerPresets
    {
        get
        {
            var presets = new ObservableCollection<ServerPresetViewModel>();
            if (!File.Exists(presetPath))
            {
                Directory.CreateDirectory(ProgramDirectory);
                File.WriteAllText(presetPath, "");
                return presets;
            }

            var lines = File.ReadAllLines(presetPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }
                
                var set = line.Split(",");
                presets.Add(new ServerPresetViewModel
                {
                    Websocket = set.ElementAtOrDefault(0),
                    FileServer = set.ElementAtOrDefault(1),
                    AdminKey = set.ElementAtOrDefault(2),
                    BackupListPath = set.ElementAtOrDefault(3) ?? "/backuplist",
                    PlacePath = set.ElementAtOrDefault(4) ?? "/place",
                    BackupsPath = set.ElementAtOrDefault(5) ?? "/backups",
                    LegacyServer = bool.TryParse(set.ElementAtOrDefault(6), out _) && bool.Parse(set.ElementAtOrDefault(6)!),
                    BackupsRepository = set.ElementAtOrDefault(7) ?? "https://github.com/rplacetk/canvas1.git"
                });
            }

            return presets;
        }
    }

    public static void SaveServerPreset(ServerPresetViewModel presetViewModel)
    {
        if (!File.Exists(presetPath))
        {
            Directory.CreateDirectory(ProgramDirectory);
            File.WriteAllText(presetPath, "");
        }

        var contents = presetViewModel.Websocket + "," + presetViewModel.FileServer + "," + presetViewModel.AdminKey + ","
            + presetViewModel.BackupListPath + "," + presetViewModel.PlacePath + "," + presetViewModel.BackupsPath + "," + presetViewModel.LegacyServer + ", "
            + presetViewModel.BackupsRepository + "\n";
        File.AppendAllText(presetPath, contents);
    }

    public static bool ServerPresetExists(ServerPresetViewModel presetViewModel)
    {
        try
        {
            var lines = File.ReadAllLines(presetPath);
            return lines.Select(line => line.Split(",")).Any(set =>
                presetViewModel.Websocket == set.ElementAtOrDefault(0) &&
                presetViewModel.FileServer == set.ElementAtOrDefault(1) &&
                presetViewModel.AdminKey == set.ElementAtOrDefault(2) &&
                presetViewModel.BackupListPath == set.ElementAtOrDefault(3) &&
                presetViewModel.PlacePath == set.ElementAtOrDefault(4) &&
                presetViewModel.BackupsPath == set.ElementAtOrDefault(5) &&
                presetViewModel.LegacyServer.ToString() == set.ElementAtOrDefault(6) &&
                presetViewModel.BackupsRepository == set.ElementAtOrDefault(7));
        }
        catch
        {
            // Ignored
        }

        return false;
    }

    [RelayCommand]
    private void SelectPaintTool()
    {
        StateInfo = App.Current.Services.GetRequiredService<PaintBrushStateInfoViewModel>();
        CurrentTool = Tool.PaintBrush;
    }

    [RelayCommand]
    private void SelectRubberTool()
    {
        StateInfo = null;
        CurrentTool = Tool.Rubber;
    }

    [RelayCommand]
    private void SelectSelectionTool()
    {
        StateInfo = null;
        CurrentTool = Tool.Select;
    }
}