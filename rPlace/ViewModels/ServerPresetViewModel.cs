using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Grpc.Core;
using rPlace.Models;

namespace rPlace.ViewModels;

public partial class ServerPresetViewModel : ObservableObject
{
    private ObservableCollection<ServerPreset> ServerPresets
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
                    Websocket = set[0], FileServer = set[1], AdminKey = set[2]
                });
            }
            return presets;
        }
    }

    public void AddServerPreset(string websocketServer, string fileServer, string adminKey)
    {
        var contents = websocketServer + Environment.NewLine + fileServer + Environment.NewLine + adminKey + Environment.NewLine;
        File.AppendAllText(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "server_presets"), contents);
    }
}
