using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using rPlace.Models;

namespace rPlace.ViewModels;

public partial class ServerPresetViewModel : ObservableObject
{
    public ObservableCollection<ServerPreset> ServerPresets
    {
        get
        {
            var presets = new ObservableCollection<ServerPreset>();
            var lines = File.ReadLines(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "server_presets"));
            var enumerable = lines as string[] ?? lines.ToArray();
            for (var i = 0; i < enumerable.Length; i += 3)
            {
                var set = enumerable[i..(i+3)];
                presets.Add(new ServerPreset
                {
                    Websocket = set[0],
                    FileServer = set[1],
                    AdminKey = set[2]
                });
            }
            return presets;
        }
    }

    public static void SaveServerPreset(ServerPreset preset)
    {
        var contents = preset.Websocket + Environment.NewLine + preset.FileServer + Environment.NewLine + preset.AdminKey + Environment.NewLine;
        File.AppendAllText(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "server_presets"), contents);
    }

    public static bool ServerPresetExists(ServerPreset preset)
    {
        try
        {
            var lines = File.ReadLines(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "server_presets"));
            var arr = lines as string[] ?? lines.ToArray();
            for (var i = 0; i < arr.Length; i += 3)
            {
                if (arr[i..(i + 3)][0] == preset.Websocket && arr[i..(i + 3)][1] == preset.FileServer && arr[i..(i + 3)][2] == preset.AdminKey)
                    return true;
            }
        }
        catch {}
        return false;
    }
}
