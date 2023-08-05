using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Net.WebSockets;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.Models;
using Websocket.Client;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Threading;
using DynamicData;
using LibGit2Sharp;
using Timer =  System.Timers.Timer;
using Avalonia.Styling;

namespace RplaceModtools.ViewModels;
public partial class MainWindowViewModel : ObservableObject
{
    // TODO: Privatise when all networking logic is moved to the VM
    public byte[]? MainChanges;
    public byte[]? MainBoard;
    public WebsocketClient? Socket;
    public TaskCompletionSource<byte[]> BoardFetchSource = new();
    private LiveChatViewModel liveChatVm = App.Current.Services.GetRequiredService<LiveChatViewModel>();
    private PaletteViewModel paletteVm = App.Current.Services.GetRequiredService<PaletteViewModel>();
    
    // TODO: Hack for now - later update to much better ObservableCollection to try and manage
    public Action<Point, Point> StartSelection;
    public Action<Selection, Point?, Point?> UpdateSelection;
    public Action<Selection> RemoveSelection;
    public Action ClearSelections;
    
    [ObservableProperty] private ThemeVariant currentTheme = ThemeVariant.Light;

    [ObservableProperty] private byte[]? board;
    [ObservableProperty] private byte[]? changes;
    [ObservableProperty] private uint canvasWidth;
    [ObservableProperty] private uint canvasHeight;
    
    [ObservableProperty] private byte[]? selectionBoard;
    [ObservableProperty] private bool viewSelectedBackupArea;
    [ObservableProperty] private Selection? currentSelection;
    // TODO: Please never use direct operations on this (we want it essentially readonly) until the actions are patched
    [ObservableProperty] private List<Selection> selections = new();
    [ObservableProperty] private SelectionHandle currentHandle = SelectionHandle.None;
    
    [ObservableProperty] private string? currentBackup;
    [ObservableProperty] private ObservableCollection<string> backups = new()
    {
        "place"
    };

    [ObservableProperty] private bool currentModerationAll;
    [ObservableProperty] private ModerationAction currentModerationAction = ModerationAction.None;
    [ObservableProperty] private TimeSpan currentModerationDuration;
    [ObservableProperty] private string currentModerationReason = "";
    [ObservableProperty] private string currentModerationUid = "";

    [ObservableProperty] private ObservableCollection<ServerPreset> serverPresets;
    [ObservableProperty] private ServerPreset currentPreset = new();
    [ObservableProperty] private int currentPaintBrushRadius = 1;
    [ObservableProperty] private BrushShape currentBrushShape = BrushShape.Square;
    [ObservableProperty] private Tool currentTool = Tool.None;

    [ObservableProperty] private ObservableCollection<ObservableObject> stateInfos = new();
    [ObservableProperty] public ObservableObject selectedStateInfo;

    // TODO: Stand-in boolean to control hiding the initial presets and other panels
    [ObservableProperty] private bool startedAndConfigured;

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
    
    public Action<Pixel> BoardSetPixel { get; set; }

    public static readonly string ProgramDirectory =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RplaceModtools");
    private static readonly string presetPath =
        Path.Join(ProgramDirectory, "server_presets.txt");
    private readonly HttpClient client = new();
    private readonly object stateInfosLock = new();
    private const int PresetVersion = 0;

    public MainWindowViewModel()
    {
        ServerPresets = LoadGeneratePresets();
    }

    [RelayCommand]
    private async Task Start()
    {
        //Configure the current session's data
        if (!ServerPresetExists(CurrentPreset))
        {
            SaveServerPreset(CurrentPreset);
        }
        
        //UI and connections
        _ = CreateConnection(UriCombine(CurrentPreset.Websocket, CurrentPreset.AdminKey));
        _ = Task.Run(async () =>
        {
            var boardPath = UriCombine(CurrentPreset.FileServer, CurrentPreset.MainBranch, CurrentPreset.PlacePath);
            var boardResponse = await Fetch(boardPath) ?? throw new Exception("Initial board load failed. Board response was null");
            BoardFetchSource.SetResult(await boardResponse.Content.ReadAsByteArrayAsync());
            //PreviewImg.Source = await CreateCanvasPreviewImage(boardResponse);
        });
        
        StartedAndConfigured = true;
        CurrentBackup = Backups.First();
        await FetchCacheBackupList();
        BackupCheckInterval();
        StateInfoInterval();
    }
    
    private async Task FetchCacheBackupList()
    {
        if (CurrentPreset!.LegacyServer)
        {
            var nameMatches = RepositoryNameRegex().Match(CurrentPreset.BackupsRepository).Groups.Values.First();
            var invalidChars = new string(Path.GetInvalidFileNameChars());
            var safeRepositoryName = Regex.Replace(nameMatches.Value, $"[{Regex.Escape(invalidChars)}]", "_");
            
            var localPath = Path.Join(ProgramDirectory, safeRepositoryName);
            if (!Directory.Exists(localPath))
            {
                Repository.Clone(CurrentPreset.BackupsRepository, localPath, new CloneOptions
                {
                    BranchName = CurrentPreset.MainBranch,
                });
            }
            
            using var backupsRepository = new Repository(localPath);
            var signature = new Signature("RplaceModtools", "rplacemodtools@unknown.com", DateTimeOffset.Now);
            var mergeResult = Commands.Pull(backupsRepository, signature, new PullOptions());

            if (mergeResult.Status == MergeStatus.Conflicts)
            {
                Console.WriteLine($"Merge failed - serious file conflict. Try deleting local backups repository? ${localPath}");
            }

            var placePath = CurrentPreset.PlacePath.StartsWith("/") 
                ? CurrentPreset.PlacePath[1..]
                : CurrentPreset.PlacePath;
            var commits = backupsRepository.Commits.QueryBy(placePath)
                .Select(commit => commit.Commit.Id.Sha).ToList();
            
            Backups = new ObservableCollection<string> { "place" };
            Backups.AddRange(commits);
        }
        else
        {
            var backupsUri = UriCombine(CurrentPreset.FileServer, CurrentPreset.BackupListPath);
            var response = await Fetch(backupsUri);
            Backups = new ObservableCollection<string> { "place" };
            if (response is null)
            {
                return;
            }
        
            var responseBody = await response.Content.ReadAsStringAsync();
            Backups.AddRange(responseBody.Split("\n"));
        }
    }
    
    private void BackupCheckInterval()
    {
        var timer = new Timer
        {
            AutoReset = true,
            Interval = TimeSpan.FromMinutes(2).TotalMilliseconds
        };

        timer.Elapsed += async (_, _) =>
        {
            await FetchCacheBackupList();
        };

        timer.Start();
    }
    
    private void StateInfoInterval()
    {
        var timer = new Timer
        {
            AutoReset = true,
            Interval = TimeSpan.FromSeconds(1).TotalMilliseconds
        };

        timer.Elapsed += (_, _) =>
        {
            lock (stateInfosLock)
            {
                var statesToRemove = StateInfos
                    .Where(info => info is ITransientStateInfo stateInfo
                        && stateInfo.SpawnedOn + stateInfo.PersistsFor < DateTime.Now)
                    .ToList();

                foreach (var stateToRemove in statesToRemove)
                {
                    StateInfos.Remove(stateToRemove);
                }
            }
        };

        timer.Start();
    }

    private ObservableCollection<ServerPreset> LoadGeneratePresets()
    {
        var presets = new ObservableCollection<ServerPreset>();
        if (!File.Exists(presetPath))
        {
            Directory.CreateDirectory(ProgramDirectory);
            File.WriteAllText(presetPath, PresetVersion + "\n");
            presets.Add(new ServerPreset());
            return presets;
        }

        var lines = File.ReadAllLines(presetPath);
        
        if (lines.Length == 0 || !int.TryParse(lines.First(), out var presetVersion) || presetVersion < PresetVersion)
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
            AddStateInfo(App.Current.Services.GetRequiredService<OutdatedPresetsStateInfoViewModel>());
            return presets;
        }
        
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }
            
            var set = line.Split(",");
            presets.Add(new ServerPreset
            {
                Websocket = set.ElementAtOrDefault(0) ?? ServerPreset.Default.Websocket,
                FileServer = set.ElementAtOrDefault(1) ?? ServerPreset.Default.FileServer,
                AdminKey = set.ElementAtOrDefault(2) ?? ServerPreset.Default.AdminKey,
                BackupListPath = set.ElementAtOrDefault(3) ?? ServerPreset.Default.BackupListPath,
                PlacePath = set.ElementAtOrDefault(4) ?? ServerPreset.Default.PlacePath,
                BackupsPath = set.ElementAtOrDefault(5) ?? ServerPreset.Default.BackupsPath,
                LegacyServer = bool.TryParse(set.ElementAtOrDefault(6), out _)
                    && bool.Parse(set.ElementAtOrDefault(6) ?? true.ToString()),
                BackupsRepository = set.ElementAtOrDefault(7) ?? ServerPreset.Default.BackupsRepository,
                MainBranch = set.ElementAtOrDefault(8) ?? ServerPreset.Default.MainBranch,
                ChatUsername = set.ElementAtOrDefault(9) ?? ServerPreset.Default.ChatUsername
            });
        }
        
        return presets;
    }

    private async Task CreateConnection(Uri uri)
    {
        var factory = new Func<ClientWebSocket>(() =>
        { 
            var wsClient = new ClientWebSocket
            {
                Options = { KeepAliveInterval = TimeSpan.FromSeconds(5) }
            };
            wsClient.Options.SetRequestHeader("Origin", "https://rplace.tk");
            return wsClient;
        });
        Socket = new WebsocketClient(uri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(10)
        };

        Socket.ReconnectionHappened.Subscribe(info =>
        {
            Console.WriteLine("Reconnected to {0}, {1}", uri, info.Type);
        });

        Socket.MessageReceived.Subscribe(msg =>
        {
            var code = msg.Binary[0];
            switch (code)
            {
                case 1:
                {
                    // New server board info packet, superceedes packet 2, also used by old board for cooldown
                    var cooldown = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[5..]);
                    if (cooldown == 0xFFFFFFFF)
                    {
                        Console.WriteLine("Canvas is locked (readonly). Edits can not be made.");
                        AddStateInfo(App.Current.Services.GetRequiredService<LockedCanvasStateInfoViewModel>());
                    }

                    // New server packs canvas width and height in code 1, making it 17, equivalent to packet 2
                    if (msg.Binary.Length == 17)
                    {
                        CanvasWidth = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[9..]);
                        CanvasHeight = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[13..]);
                        
                        Task.Run(async () =>
                        {
                            Board = MainBoard = BoardPacker.RunLengthDecompressBoard(
                                await BoardFetchSource.Task, (int)(CanvasWidth * CanvasHeight));
                        });
                    }
                    break;
                }
                case 2:
                {
                    // Old board changes packet (contains board info and changes since fetched place file)
                    CanvasWidth = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[1..]);
                    CanvasHeight = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[5..]);
                    Task.Run(async () =>
                    {
                        Board = MainBoard = await BoardFetchSource.Task;
                        Changes = MainChanges = RunLengthChanges(msg.Binary.AsSpan()[9..]);
                    });
                    break;
                }
                case 6: //Incoming pixel someone else sent
                {
                    var i = 0;
                    while (i < msg.Binary.Length - 2)
                    {
                        var index = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[(i += 1)..]);
                        var colour = msg.Binary[i += 4];
                        BoardSetPixel(new Pixel(index, colour));
                        // TODO: Find a way to implement calling Board.Set from the VM (likely through an action/delegate to the view)
                    }
                    break;
                }
                case 15: // 15 = chat
                {
                    var msgData = Encoding.UTF8.GetString(msg.Binary.AsSpan()[1..]).Split("\n");
                    var message = msgData[0];
                    var name = msgData[1];
                    var channelName = msgData[2];
                    var type = msgData.ElementAtOrDefault(3);
                    var x = msgData.ElementAtOrDefault(4);
                    var y = msgData.ElementAtOrDefault(5);
                    var uid = msgData.ElementAtOrDefault(6);
                    
                    if (type is null or "live")
                    {
                        Dispatcher.UIThread.Post(() => // TOD: Likely not needed now
                        {
                            var channelViewModel = liveChatVm.Channels.FirstOrDefault(channel => channel.ChannelName == channelName);
                            if (channelViewModel is null)
                            {
                                channelViewModel = new LiveChatChannelViewModel(channelName);
                                liveChatVm.Channels.Add(channelViewModel);
                            }

                            if (channelViewModel.Messages.Count > 100)
                            {
                                channelViewModel.Messages.RemoveAt(0);
                            }
                            
                            channelViewModel.Messages.Add(new ChatMessage
                            {
                                Message = message,
                                Name = name,
                                Uid = uid
                            });
                        });
                    }
                    else
                    {
                        //Console.WriteLine($"Could not handle message: [{msgData[1]}/{msgData[2]}{(uid == null ? "" : "/" + uid)}] {msgData[0]}");
                    }
                    break;
                }
            }
        });
        Socket.DisconnectionHappened.Subscribe(info => Console.WriteLine("Disconnected from {0}, {1}", uri, info.Exception));
        
        await Socket.Start();
    }

    //Decompress changes so it can be put onto canv
    private byte[] RunLengthChanges(Span<byte> data)
    {
        // TODO: Fix possible misalignment causing messed up changes
        var i = 0;
        var changeI = 0;
        var changes = new byte[(int) (CanvasWidth * CanvasHeight)];

        while (i < data.Length)
        {
            var cell = data[i++];
            var repeats = cell >> 6;
            switch (repeats)
            {
                case 1:
                    repeats = data[i++];
                    break;
                case 2:
                    repeats = BinaryPrimitives.ReadUInt16BigEndian(data[i++..]);
                    i++;
                    break;
                case 3:
                    repeats = (int) BinaryPrimitives.ReadUInt32BigEndian(data[i++..]);
                    i += 3;
                    break;
            }
            changeI += repeats;
            changes[changeI] = (byte) (cell & 63);
        }
        
        return changes;
    }

    partial void OnSelectedStateInfoChanged(ObservableObject? oldValue, ObservableObject? newValue)
    {
        lock (stateInfosLock)
        {
            if (newValue is not null)
            {
                StateInfos.Remove(newValue);
            }
        }
    }
    
    public static void SaveServerPreset(ServerPreset preset)
    {
        if (!File.Exists(presetPath))
        {
            Directory.CreateDirectory(ProgramDirectory);
            File.WriteAllText(presetPath, PresetVersion + "\n");
        }

        var contents = preset.Websocket + "," + preset.FileServer + "," + preset.AdminKey + ","
            + preset.BackupListPath + "," + preset.PlacePath + "," + preset.BackupsPath + "," + preset.LegacyServer + ","
            + preset.BackupsRepository + "," + preset.MainBranch + "," + preset.ChatUsername + "\n";
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
    private void ToggleTheme()
    {
        CurrentTheme = CurrentTheme == ThemeVariant.Light ? ThemeVariant.Dark : ThemeVariant.Light; 
    }

    [RelayCommand]
    private void NextBackup()
    {
        CurrentBackup = Backups[Math.Max(Backups.IndexOf(CurrentBackup) - 1, 0)];
    }

    [RelayCommand]
    private void PreviousBackup()
    {
        CurrentBackup = Backups[Math.Min(Backups.IndexOf(CurrentBackup) + 1, Backups.Count - 1)];
    }

    [RelayCommand]
    private void SelectPaintTool()
    {
        RemoveToolStateInfos();
        AddStateInfo(App.Current.Services.GetRequiredService<PaintBrushStateInfoViewModel>());
        CurrentTool = Tool.PaintBrush;
    }

    [RelayCommand]
    private void SelectRubberTool()
    {
        CurrentTool = Tool.Rubber;
    }

    [RelayCommand]
    private void SelectSelectionTool()
    {
        RemoveToolStateInfos();
        AddStateInfo(App.Current.Services.GetRequiredService<SelectStateInfoViewModel>());
        CurrentTool = Tool.Select;
    }

    private void RemoveToolStateInfos()
    {
        RemoveStateInfo(App.Current.Services.GetRequiredService<PaintBrushStateInfoViewModel>());
        RemoveStateInfo(App.Current.Services.GetRequiredService<SelectStateInfoViewModel>());
    }

    [RelayCommand]
    private void ViewSelectedBackup()
    {
        if (ViewSelectedBackupArea)
        {
            Task.Run(() => ViewCanvasBackupSelection(CurrentBackup));
        }
        else
        {
            Task.Run(() => ViewCanvasAtBackup(CurrentBackup));
        }
    }

    [RelayCommand]
    private void RollbackSelectedArea()
    {
        if (SelectionBoard is null)
        {
            Console.WriteLine("ERROR: Currently you need to enable 'view selected area at canvas backup' in order to rollback.");
            return;
        }

        foreach (var selection in Selections)
        {
            Rollback((int) selection.TopLeft.X, (int) selection.TopLeft.Y, (int) selection.BottomRight.X - (int) selection.TopLeft.X, 
                (int) selection.BottomRight.Y - (int) selection.TopLeft.Y, SelectionBoard);
        }        

    }
    
    private void Rollback(int x, int y, int regionWidth, int regionHeight, byte[] rollbackBoard)
    {
        // TODO: Improve range checks here (even though server will likely fix regardless)
        if (regionWidth > 250 || regionHeight > 250 || x >= CanvasWidth || y >= CanvasHeight)
        {
            return;
        }
        
        var buffer = new byte[6 + regionWidth * regionHeight];
        var i = (int) (x + y * CanvasWidth);
        
        // Setting up first 7 bytes of metadata
        new byte[]
        {
            99, (byte) regionWidth, (byte) (i >> 24), (byte) (i >> 16), (byte) (i >> 8), (byte) i
        }.CopyTo(buffer, 0);
        
        // Copy over just that region of the board into the buffer we send to server
        for (var row = 0; row < regionHeight; row++)
        {
            Buffer.BlockCopy(rollbackBoard, i, buffer, 6 + row * regionWidth, regionWidth);
            i += (int) CanvasWidth;
        }

        if (Socket is { IsRunning: true })
        {
            Socket.Send(buffer);
        }
    }

    partial void OnCurrentBackupChanged(string? oldValue, string? newValue)
    {
        Task.Run(async () =>
        {
            if (newValue is null || oldValue == newValue || MainBoard is null)
            {
                return;
            }

            if (newValue == "place")
            {
                await ViewMainCanvas();
            }
            else if (ViewSelectedBackupArea)
            {
                await ViewCanvasBackupSelection(newValue);
            }
            else
            {
                await ViewCanvasAtBackup(newValue);
            }
        });
    }

    /// <summary>
    /// Will set the board to a cache version of the current main canvas
    /// </summary>
    /// <exception cref="Exception">Occurs if main board is null
    /// Will only be the case if this method is attempted to be called before initial board load.</exception>
    private async Task ViewMainCanvas()
    {
        Board = MainBoard ?? throw new Exception("Can not view main canvas. Main board from initial fetch was null.");
        Changes = MainChanges;
        //PreviewImg.Source = await CreateCanvasPreviewImage(mainBoard) ?? new Bitmap("../Assets/preview_default.png");
    }

    /// <summary>
    /// Views the whole canvas at a certain date/backup. Unlike ViewCanvasSelectionBackup there is no portalling at all
    /// through the current selection.
    /// </summary>
    private async Task ViewCanvasAtBackup(string backupName)
    {
        var backupUri = CurrentPreset.LegacyServer
            ? UriCombine(CurrentPreset.FileServer, backupName, CurrentPreset.PlacePath)
            : UriCombine(CurrentPreset.FileServer, CurrentPreset.BackupsPath, backupName);
        
        var boardResponse = await Fetch(backupUri);
        if (boardResponse is null)
        {
            Console.WriteLine($"FATAL: Could not load place backup {backupName}");
            await ViewMainCanvas();
            return;
        }
        
        Board = await boardResponse.Content.ReadAsByteArrayAsync();
        Changes = null;
        
        AddStateInfo(App.Current.Services.GetRequiredService<LiveCanvasStateInfoViewModel>());
        //PreviewImg.Source = await CreateCanvasPreviewImage(backupUri) ?? new Bitmap("../Assets/preview_default.png");
    }

    /// <summary>
    /// Views the canvas with the board being predominantly at the live/current date, but will also hold the selected  
    /// backup inside of the selections render pass, so that by selecting an area you can see a 'portal' into that older
    /// canvas backup.
    /// </summary>
    private async Task ViewCanvasBackupSelection(string backupName)
    {
        await ViewMainCanvas();
        
        var backupUri = CurrentPreset.LegacyServer
            ? UriCombine(CurrentPreset.FileServer, backupName, CurrentPreset.PlacePath)
            : UriCombine(CurrentPreset.FileServer, CurrentPreset.BackupsPath, backupName);
        
        var backupResponse = await Fetch(backupUri);
        var backupTask = backupResponse?.Content.ReadAsByteArrayAsync();
        if (backupTask is not null)
        {
            SelectionBoard = await backupTask;
        }
    }

    private void AddStateInfo(ObservableObject stateInfo)
    {
        lock (stateInfosLock)
        {
            if (StateInfos.Contains(stateInfo))
            {
                if (stateInfo is ITransientStateInfo transientStateInfo)
                {
                    transientStateInfo.SpawnedOn = DateTime.Now;
                }
                
                return;
            }

            StateInfos.Add(stateInfo);
        }
    }

    private void RemoveStateInfo(ObservableObject stateInfo)
    {
        lock (stateInfosLock)
        {
            StateInfos.Remove(stateInfo);
        }
    }

    private static Uri UriCombine(params string[] parts)
    {
        return new Uri(string.Join("/", parts
            .Where(part => !string.IsNullOrEmpty(part))
            .Select(subPath => subPath.Trim('/'))
            .ToArray()));
    }

    public async Task<HttpResponseMessage?> Fetch(Uri uri)
    {
        try
        {
            var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch (HttpRequestException) { }
        
        return null;
    }
    
    [GeneratedRegex(@"github.com\/[\w\-]+\/([\w\-]+)")]
    private static partial Regex RepositoryNameRegex();
}