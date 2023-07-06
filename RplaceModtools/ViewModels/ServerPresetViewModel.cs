using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RplaceModtools.Models;

namespace RplaceModtools.ViewModels;

public partial class ServerPresetViewModel : ObservableObject
{
    private static readonly string presetPath = 
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RplaceModtools", "server_presets.txt");
    
    public ObservableCollection<ServerPreset> ServerPresets
    {
        get
        {
            var presets = new ObservableCollection<ServerPreset>();
            var lines = File.ReadAllLines(presetPath);
            for (var i = 0; i < lines.Length; i += 5)
            {
                var set = lines[i..(i + 5)];
                presets.Add(new ServerPreset
                {
                    Websocket = set.ElementAtOrDefault(0),
                    FileServer = set.ElementAtOrDefault(1),
                    AdminKey = set.ElementAtOrDefault(2),
                    BackupListPath = set.ElementAtOrDefault(3),
                    PlacePath = set.ElementAtOrDefault(4)
                });
            }
            return presets;
        }
    }

    public static void SaveServerPreset(ServerPreset preset)
    {
        var contents = preset.Websocket + Environment.NewLine
            + preset.FileServer + Environment.NewLine
            + preset.AdminKey + Environment.NewLine
            + preset.BackupListPath + Environment.NewLine
            + preset.PlacePath + Environment.NewLine;
        File.AppendAllText(presetPath, contents);
    }

    public static bool ServerPresetExists(ServerPreset preset)
    {
        try
        {
            var lines = File.ReadAllLines(presetPath);
            for (var i = 0; i < lines.Length; i += 5)
            {
                if (lines[i] == preset.Websocket
                    && lines[i + 1] == preset.FileServer
                    && lines[i + 2] == preset.AdminKey
                    && lines[i + 2] == preset.BackupListPath
                    && lines[i + 2] == preset.PlacePath)
                {
                    return true;
                }
            }
        }
        catch {}
        return false;
    }
}
