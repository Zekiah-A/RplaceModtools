using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;

public partial class ServerPresetViewModel : ObservableObject
{
    private static readonly string presetDirectory =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RplaceModtools");
    private static readonly string presetPath =
        Path.Join(presetDirectory, "server_presets.txt");
    
    public ObservableCollection<ServerPreset> ServerPresets
    {
        get
        {
            var presets = new ObservableCollection<ServerPreset>();
            if (!File.Exists(presetPath))
            {
                Directory.CreateDirectory(presetDirectory);
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
                presets.Add(new ServerPreset
                {
                    Websocket = set.ElementAtOrDefault(0),
                    FileServer = set.ElementAtOrDefault(1),
                    AdminKey = set.ElementAtOrDefault(2),
                    BackupListPath = set.ElementAtOrDefault(3) ?? "/backuplist",
                    PlacePath = set.ElementAtOrDefault(4) ?? "/place",
                    BackupsPath = set.ElementAtOrDefault(5) ?? "/backups"
                });
            }

            return presets;
        }
    }

    public static void SaveServerPreset(ServerPreset preset)
    {
        if (!File.Exists(presetPath))
        {
            Directory.CreateDirectory(presetDirectory);
            File.WriteAllText(presetPath, "");
        }

        var contents = preset.Websocket + "," + preset.FileServer + "," + preset.AdminKey + ","
            + preset.BackupListPath + "," + preset.PlacePath + "," + preset.BackupsPath + "\n";
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
                preset.BackupsPath == set.ElementAtOrDefault(5));
        }
        catch
        {
            // Ignored
        }

        return false;
    }
}
