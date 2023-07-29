using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;
public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private bool viewSelectedBackupArea;
    [ObservableProperty] private string? currentBackup;
    
    [ObservableProperty] private bool currentModerationAll;
    [ObservableProperty] private ModerationAction currentModerationAction = ModerationAction.None;
    [ObservableProperty] private TimeSpan currentModerationDuration;
    [ObservableProperty] private string currentModerationReason = "";
    [ObservableProperty] private string currentModerationUid = "";

    [ObservableProperty] private ServerPreset? currentPreset;
    [ObservableProperty] private int currentPaintBrushRadius = 1;
    [ObservableProperty] private Shape currentBrushShape = Shape.Square;
    [ObservableProperty] private ObservableCollection<ObservableObject> stateInfos = new();
    [ObservableProperty] private Tool currentTool = Tool.None;

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
        "https://raw.githubusercontent.com/rplacetk/canvas1/",
    };
    public string? ChatUsername { get; set; }

    public static readonly string ProgramDirectory =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RplaceModtools");
    private static readonly string presetPath =
        Path.Join(ProgramDirectory, "server_presets.txt");
    private const int PresetVersion = 0;
    
    public ObservableCollection<ServerPreset> ServerPresets
    {
        get
        {
            var presets = new ObservableCollection<ServerPreset>();
            if (!File.Exists(presetPath))
            {
                Directory.CreateDirectory(ProgramDirectory);
                File.WriteAllText(presetPath, PresetVersion.ToString() + "\n");
                return presets;
            }

            var lines = File.ReadAllLines(presetPath);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (i == 0)
                {
                    if (!int.TryParse(line, out var presetVersion) || presetVersion < PresetVersion)
                    {
                        Console.WriteLine("Preset version was below current preset version, dumping and upgrading");
                        Console.WriteLine("Old presets:" + string.Join("  \n", lines));
                        
                        var oldPresetsPath = Path.Join(Directory.GetCurrentDirectory(), "server_presets_old.txt");
                        if (File.Exists(oldPresetsPath))
                        {
                            File.Delete(oldPresetsPath);
                        }

                        File.Move(presetPath, oldPresetsPath);
                        presets.Add(new ServerPreset());
                        StateInfos.Add(App.Current.Services.GetRequiredService<OutdatedPresetsStateInfoViewModel>());
                        break;
                    }
                    
                    continue;
                }

                var set = line.Split(",");
                presets.Add(new ServerPreset
                {
                    Websocket = set.ElementAtOrDefault(0)!,
                    FileServer = set.ElementAtOrDefault(1)!,
                    AdminKey = set.ElementAtOrDefault(2)!,
                    BackupListPath = set.ElementAtOrDefault(3) ?? "/backuplist",
                    PlacePath = set.ElementAtOrDefault(4) ?? "/place",
                    BackupsPath = set.ElementAtOrDefault(5) ?? "/backups",
                    LegacyServer = bool.TryParse(set.ElementAtOrDefault(6), out _) && bool.Parse(set.ElementAtOrDefault(6)!),
                    BackupsRepository = set.ElementAtOrDefault(7) ?? "https://github.com/rplacetk/canvas1.git",
                    MainBranch = set.ElementAtOrDefault(8) ?? "main"
                });
            }
            
            return presets;
        }
    }

    public RelayCommand<ObservableObject> DeleteStateInfoCommand { get; }
    
    public MainWindowViewModel()
    {
        DeleteStateInfoCommand = new RelayCommand<ObservableObject>(DeleteStateInfo);
    }

    private void DeleteStateInfo(ObservableObject? stateInfo)
    {
        StateInfos.Remove(stateInfo);
    }

    public static void SaveServerPreset(ServerPreset preset)
    {
        if (!File.Exists(presetPath))
        {
            Directory.CreateDirectory(ProgramDirectory);
            File.WriteAllText(presetPath, PresetVersion.ToString() + "\n");
        }

        var contents = preset.Websocket + "," + preset.FileServer + "," + preset.AdminKey + ","
            + preset.BackupListPath + "," + preset.PlacePath + "," + preset.BackupsPath + "," + preset.LegacyServer + ","
            + preset.BackupsRepository + "," + preset.MainBranch + "\n";
        File.AppendAllText(presetPath, contents);
    }

    public static bool ServerPresetExists(ServerPreset preset)
    {
        try
        {
            var lines = File.ReadAllLines(presetPath);
            return lines.Select(line => line.Split(",")).Any(set =>
                preset.Websocket == set.ElementAtOrDefault(0) &&
                preset.FileServer == set.ElementAtOrDefault(1) &&
                preset.AdminKey == set.ElementAtOrDefault(2) &&
                preset.BackupListPath == set.ElementAtOrDefault(3) &&
                preset.PlacePath == set.ElementAtOrDefault(4) &&
                preset.BackupsPath == set.ElementAtOrDefault(5) &&
                preset.LegacyServer.ToString() == set.ElementAtOrDefault(6) &&
                preset.BackupsRepository == set.ElementAtOrDefault(7) &&
                preset.MainBranch == set.ElementAtOrDefault(8));
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
        //StateInfo = App.Current.Services.GetRequiredService<PaintBrushStateInfoViewModel>();
        CurrentTool = Tool.PaintBrush;
    }

    [RelayCommand]
    private void SelectRubberTool()
    {
        //StateInfo.Remove();
        CurrentTool = Tool.Rubber;
    }

    [RelayCommand]
    private void SelectSelectionTool()
    {
        //StateInfo.Remove();
        CurrentTool = Tool.Select;
    }
}