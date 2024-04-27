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
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DynamicData;
using LibGit2Sharp;
using Timer = System.Timers.Timer;
using Avalonia.Styling;
using RplaceModtools.Views;
using SkiaSharp;
using System.Text.Json;
using System.Net.Http.Json;
using System.Web;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

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
    public Func<Point, Point, Selection> StartSelection;
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
    [ObservableProperty] private uint currentModerationMessageId;

    [ObservableProperty] private ObservableCollection<ServerPreset> serverPresets;
    [ObservableProperty] private ServerPreset currentPreset = new();
    [ObservableProperty] private int currentPaintBrushRadius = 1;
    [ObservableProperty] private BrushShape currentBrushShape = BrushShape.Square;
    [ObservableProperty] private Tool currentTool = Tool.None;

    [ObservableProperty] private ObservableCollection<ObservableObject> stateInfos = new();
    [ObservableProperty] public ObservableObject selectedStateInfo;
    [ObservableProperty] private Bitmap previewImageSource =
        new Bitmap(AssetLoader.Open(new Uri("avares://RplaceModtools/Assets/preview_default.png")));
    [ObservableProperty] private bool startedAndConfigured;
    
    // Image canvas paste stuff
    [ObservableProperty] private string loadImageStatus = "No image loaded";
    [ObservableProperty] private int imageWidth;
    [ObservableProperty] private int imageHeight;
    [ObservableProperty] private int imageX;
    [ObservableProperty] private int imageY;
    [ObservableProperty] private IImage imagePreview;
    
    // Github code panel
    [ObservableProperty] private bool githubCodePanelVisible;
    [ObservableProperty] private string? githubCode;
    private bool githubCodeAuthCancelled = false;

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
    public Action<int> BoardUnsetPixel { get; set; }

    private const string GithubClientId = "3b37d31a1a87dd16d8e3";
    public static readonly string ProgramDirectory =
        Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RplaceModtools");
    private static readonly string presetPath =
        Path.Join(ProgramDirectory, "server_presets.txt");
    private readonly HttpClient client = new();
    private readonly object stateInfosLock = new();
    private const int PresetVersion = 0;
    private readonly Dictionary<uint, uint> intIdPositions = new();
    private readonly Dictionary<uint, string> intIdNames = new();
    private uint intId;
    public uint Cooldown;
    private GithubAccessToken? accessToken;

    public MainWindowViewModel()
    {
        ServerPresets = LoadGeneratePresets();
    }

    [RelayCommand]
    private void Start()
    {
        // Stop debounce
        if (StartedAndConfigured)
        {
            return;
        }
        
        //Configure the current session's data
        if (!ServerPresetExists(CurrentPreset))
        {
            SaveServerPreset(CurrentPreset);
        }
        
        //UI and connections, the second and third methods here mind-blowingly heavy and therefore run on separate threads
        _ = CreateConnection(UriCombine(CurrentPreset.Websocket, CurrentPreset.AdminKey));
        _ = Task.Run(async () =>
        {
            var boardNotification = App.Current.Services.GetRequiredService<NotificationStateInfoViewModel>();
            AddStateInfo(boardNotification);
            boardNotification.PersistsFor = TimeSpan.MaxValue;
            boardNotification.Notification = "Downloading current canvas";

            var boardPath = UriCombine(CurrentPreset.FileServer, CurrentPreset.MainBranch, CurrentPreset.PlacePath);
            var boardResponse = await client.GetAsync(boardPath);
            if (!boardResponse.IsSuccessStatusCode)
            {
                throw new Exception("Initial board load failed. Board response was null");
            }
            BoardFetchSource.SetResult(await boardResponse.Content.ReadAsByteArrayAsync());
            RemoveStateInfo(boardNotification);

            //PreviewImg.Source = await CreateCanvasPreviewImage(boardResponse);
        });
        _ = Task.Run(FetchCacheBackupList);

        StartedAndConfigured = true;
        CurrentBackup = Backups.First();
        
        BackupCheckInterval();
        StateInfoInterval();
    }
    
    private async Task<Bitmap?> CreateCanvasPreviewImage<T>(T input)
    {
        async Task<byte[]?> FetchBoardAsync(Uri uri)
        {
            var boardResponse = await client.GetAsync(uri);
            if (!boardResponse.IsSuccessStatusCode)
            {
                return await boardResponse.Content.ReadAsByteArrayAsync();
            }
            
            return null;
        }
        
        var placeFile = input switch
        {
            Uri uri => await FetchBoardAsync(uri),
            byte[] board => board,
            _ => null
        };
        if (placeFile is null)
        {
            return null;
        }
        
        var imageInfo = new SKImageInfo((int) CanvasWidth, (int) CanvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        Parallel.For(0, placeFile.Length, i =>
        {
            var x = (int) (i % CanvasWidth);
            var y = (int) (i / CanvasWidth);
            canvas.DrawPoint(x, y, paletteVm.PaletteColours.ElementAtOrDefault(placeFile[i]));
        });
        
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null)
        {
            return null;
        }
        
        var imageStream = data.AsStream();
        imageStream.Seek(0, SeekOrigin.Begin);
        var imageBitmap = new Bitmap(imageStream);
        await imageStream.FlushAsync();
        await imageStream.DisposeAsync();
        return imageBitmap;
    }

    private async Task FetchCacheBackupList()
    {
        var progressNotification = App.Current.Services.GetRequiredService<NotificationStateInfoViewModel>();
        AddStateInfo(progressNotification);
        progressNotification.PersistsFor = TimeSpan.MaxValue;
        progressNotification.Notification = "Started loading canvas backup list...";
        var nameMatches = RepositoryNameRegex().Match(CurrentPreset.BackupsRepository).Groups.Values.First();
        var invalidChars = new string(Path.GetInvalidFileNameChars());
        var safeRepositoryName = Regex.Replace(nameMatches.Value, $"[{Regex.Escape(invalidChars)}]", "_");

        if (CurrentPreset.LegacyServer)
        {
            Backups = new ObservableCollection<string> { "place" };

            // Fast path - Use github API (git cloning can be unbeliavably slow)
            if (CurrentPreset.BackupsRepository.Contains("github.com"))
            {
                var jsonOptions = new JsonSerializerOptions()
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                };
                if (accessToken is null)
                {
                    // 1 - Get github authorisation                    
                    var codeRequestObject = new { ClientId = GithubClientId, Scope = "public_repo" };
                    var jsonHeaderValue = MediaTypeHeaderValue.Parse("application/json");
                    var codeRequestContent = JsonContent.Create(codeRequestObject, jsonHeaderValue, jsonOptions);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",  "application/json");
                    var codeResponse = await client.PostAsync("https://github.com/login/device/code", codeRequestContent);
                    client.DefaultRequestHeaders.Clear();

                    if (!codeResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Failed to load git commits from github API, HTTP response was not okay");
                        RemoveStateInfo(progressNotification);
                        return;
                    }
                    var code = await codeResponse.Content.ReadFromJsonAsync<GithubApiCode>(jsonOptions);
                    if (code is null)
                    {
                        Console.WriteLine("Failed to load git commits from github API, HTTP response was not okay");
                        RemoveStateInfo(progressNotification);
                        return;
                    }

                    GithubCodePanelVisible = true;
                    GithubCode = code.UserCode;

                    // 2 - Open browser
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        Process.Start("explorer", code.VerificationUri);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        Process.Start("xdg-open", code.VerificationUri);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        Process.Start("open", code.VerificationUri);
                    }

                    // 3 - Test authorisation
                    var tries = 1;
                    var authStart = DateTime.Now;
                    while ((DateTime.Now - authStart).TotalSeconds < code.ExpiresIn && !githubCodeAuthCancelled)
                    {
                        var accessRequestObject = new GithubAccessRequest(GithubClientId, code.DeviceCode, "urn:ietf:params:oauth:grant-type:device_code");
                        var accessRequestContent = JsonContent.Create(accessRequestObject, jsonHeaderValue, jsonOptions);
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
                        var accessResponse = await client.PostAsync("https://github.com/login/oauth/access_token", accessRequestContent);
                        client.DefaultRequestHeaders.Clear();
                        if (accessResponse.IsSuccessStatusCode)
                        {
                            var accessResponseText = await accessResponse.Content.ReadAsStringAsync();
                            var error = JsonSerializer.Deserialize<GithubApiError>(accessResponseText, jsonOptions);
                            if (error is null || string.IsNullOrEmpty(error.Error))
                            {
                                progressNotification.Notification =
                                    $"Downloading canvas backup: (waiting for github auth) {tries}";
                                accessToken = JsonSerializer.Deserialize<GithubAccessToken>(accessResponseText, jsonOptions);
                                if (accessToken is not null)
                                {
                                    GithubCode = string.Empty;
                                    GithubCodePanelVisible = false;
                                    break;
                                }
                            }
                        }
                        
                        await Task.Delay(code.Interval * 1000);
                        tries++;
                    }
                }
                
                // Load canvas commit hashes from locally saved caches file
                var cachePath = Path.Join(ProgramDirectory, safeRepositoryName + ".txt");
                var caches = new List<string>();
                DateTime? cacheStartDate = null;
                if (File.Exists(cachePath))
                {
                    using var cacheReader = new StreamReader(cachePath);
                    while (await cacheReader.ReadLineAsync() is { } line)
                    {
                        // Invalid data
                        if (string.IsNullOrEmpty(line))
                        {
                            continue;
                        }
                        // Metadata cache region
                        if (cacheStartDate is null)
                        {
                            cacheStartDate = DateTime.Parse(line);
                            continue;
                        }
                        // Add cache
                        caches.Add(line);
                    }
                }

                if (!githubCodeAuthCancelled)
                {
                    // Pull commit hashes with API - first save to a tmp file, then merge with existing commit hashes
                    var page = 1;
                    var commitCount = 0;
                    var newLocation = CurrentPreset.BackupsRepository.Replace("github.com", "api.github.com/repos");
                    // To avoid duplicates from after cache skip (likely not aligned to current pages)
                    var cacheTempPath = cachePath + ".tmp";
                    var newCommits = new List<string>();
                    DateTime? newCommitStartDate = null;
                    const int pageReadLength = 100;
                    
                    await using var tempStream = File.OpenWrite(cacheTempPath);
                    while (true)
                    {
                        var url = new Uri($"{newLocation}/commits?per_page={pageReadLength}&page={page}");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
                        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RplaceModtools");
                        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.AccessToken);
                        var response = await client.GetAsync(url);
                        client.DefaultRequestHeaders.Clear();
                        if (response is null || !response.IsSuccessStatusCode)
                        {
                            Console.WriteLine("Failed to load git commits from github API, HTTP response was not okay: "
                                + await response?.Content.ReadAsStringAsync());
                            RemoveStateInfo(progressNotification);
                            return;
                        }
                        var commits = await response.Content.ReadFromJsonAsync<GithubApiCommitItem[]>(jsonOptions);
                        if (commits is null || commits.Length == 0)
                        {
                            break;
                        }

                        foreach (var commit in commits)
                        {
                            // We are no longer reading new commits and have wandered into the caches
                            var commitDate = commit.Commit.Committer.Date;
                            if (DateTime.Parse(commitDate) < cacheStartDate)
                            {
                                goto FinishAndWrite;
                            }
                            if (newCommitStartDate is null)
                            {
                                newCommitStartDate = DateTime.Parse(commitDate);
                                await tempStream.WriteAsync(Encoding.UTF8.GetBytes(newCommitStartDate + "\n"));
                            }
                            newCommits.Add(commit.Sha);
                            await tempStream.WriteAsync(Encoding.UTF8.GetBytes(commit.Sha + "\n"));
                            progressNotification.Notification =
                                $"Downloading canvas backups from github: {commitCount} commits";
                            commitCount++;
                        }
                        page++;
                    }
                    FinishAndWrite:
                    foreach (var cache in caches)
                    {
                        await tempStream.WriteAsync(Encoding.UTF8.GetBytes(cache + "\n"));
                    }
                    await tempStream.FlushAsync();
                    File.Move(cacheTempPath, cachePath, true);
                    Backups.AddRange(newCommits);
                    progressNotification.ResetPersistsTo(TimeSpan.FromSeconds(1));
                }
                else
                {
                    progressNotification.Notification = "Skipped loading new canvas backups from git. Using local cached backups (may be outdated)";
                    progressNotification.ResetPersistsTo(TimeSpan.FromSeconds(5));
                }
                Backups.AddRange(caches);
                return;
            }

            try
            {
                var localPath = Path.Join(ProgramDirectory, safeRepositoryName);
                // Clear up incomplete clones
                if (Directory.Exists(localPath))
                {
                    var contents = Directory.GetFileSystemEntries(localPath);
                    if (contents.Length == 1 && Path.GetFileName(contents[0]) == ".git")
                    {
                        Directory.Delete(localPath, true);
                    }
                }
                // Clone repo (will take a long time)
                if (!Directory.Exists(localPath))
                {
                    var cloneOptions = new CloneOptions
                    {
                        BranchName = CurrentPreset.MainBranch,
                        IsBare = false,
                        OnCheckoutProgress = (path, completedSteps, totalSteps) =>
                        {
                            progressNotification.Notification =
                                $"Downloading canvas backups repository: (completed) {completedSteps}/{totalSteps} steps";
                        }
                    };
                    cloneOptions.FetchOptions.OnTransferProgress = (transfer) =>
                    {
                        var percent = Math.Round((double) transfer.ReceivedObjects / transfer.TotalObjects, 2);
                        progressNotification.Notification =
                            $"Downloading canvas backups repository: (cloning) {percent}%";
                        return true;
                    };
                    Repository.Clone(CurrentPreset.BackupsRepository, localPath, cloneOptions);
                }
                using var backupsRepository = new Repository(localPath);
                // HACK: For some reason it may show no local branches
                /*if (backupsRepository.Branches[CurrentPreset.MainBranch] is null)
                {
                    Console.WriteLine("WARN: Need to create local branch.");
                    var localBranch = backupsRepository.CreateBranch(CurrentPreset.MainBranch);
                    var trackBranch = "refs/remotes/origin/" + CurrentPreset.MainBranch; // Upstream branch
                    backupsRepository.Branches.Update(localBranch, branchUpdater => branchUpdater.TrackedBranch = trackBranch);
                    Console.WriteLine("WARN: Created local branch.");
                }*/
                var signature = new Signature("RplaceModtools", "modtools@rplace.live", DateTimeOffset.Now);
                var mergeResult = Commands.Pull(backupsRepository, signature, new PullOptions()
                {
                    FetchOptions = new FetchOptions()
                    {
                        OnTransferProgress = progress =>
                        {
                            var percent = progress.ReceivedObjects / (float) progress.TotalObjects * 100;
                            progressNotification.Notification = $"Fetching latest updates from backups repository: {percent}%";
                            return true;
                        },

                    },
                    MergeOptions = new MergeOptions()
                    {
                        MergeFileFavor = MergeFileFavor.Theirs
                    }
                });

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
            catch (Exception exception)
            {
                progressNotification.Notification = "Failed to load canvas backups";
                Console.WriteLine($"Failed to load canvas backups: {exception}");
            }
        }
        else
        {
            var backupsUri = UriCombine(CurrentPreset.FileServer, CurrentPreset.BackupListPath);
            var response = await client.GetAsync(backupsUri);
            response.EnsureSuccessStatusCode();
            Backups = new ObservableCollection<string> { "place" };

            var responseBody = await response.Content.ReadAsStringAsync();
            Backups.AddRange(responseBody.Split("\n"));
        }
        
        RemoveStateInfo(progressNotification);
    }
    
    private void BackupCheckInterval()
    {
        // TODO: This is messy - fix
        /*var timer = new Timer
        {
            AutoReset = true,
            Interval = TimeSpan.FromMinutes(2).TotalMilliseconds
        };

        timer.Elapsed += async (_, _) =>
        {
            await FetchCacheBackupList();
        };

        timer.Start();*/
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
                try
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
                catch (Exception)
                {
                    // Ignore
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
            var outdatedPresets = App.Current.Services.GetRequiredService<NotificationStateInfoViewModel>();
            AddStateInfo(outdatedPresets);
            outdatedPresets.PersistsFor = TimeSpan.FromSeconds(15);
            outdatedPresets.Notification =
                $"⚠ Warning: Outdated server presets found. New presets file has been generated, and old presets moved to: '{oldPresetsPath}'";

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
            wsClient.Options.SetRequestHeader("Origin", "https://rplace.live");
            wsClient.Options.SetRequestHeader("User-Agent", "RplaceModtools");
            return wsClient;
        });
        Socket = new WebsocketClient(uri, factory)
        {
            ReconnectTimeout = TimeSpan.FromSeconds(10)
        };
        Socket.MessageReceived.Subscribe(msg =>
        {
            var data = msg.Binary.AsSpan();
            var code = msg.Binary[0];
            switch (code)
            {
                case 0:
                {
                    var newPalette = MemoryMarshal.Cast<byte, uint>(msg.Binary.AsSpan()[1..]).ToArray();
                    Dispatcher.UIThread.Post(() => paletteVm.UpdatePalette(newPalette.ToArray()));
                    break;
                }
                case 1:
                {
                    // New server board info packet, supercedes packet 2, also used by old board for cooldown
                    Cooldown = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[5..]);

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
                case 5: // Incoming pixel with placer intId name
                {
                    var i = 1;
                    while (i < msg.Binary.Length)
                    {
                        var position = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan(i)); i += 4;
                        var colour = msg.Binary[i++];
                        var intId = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan(i)); i += 4;

                        BoardSetPixel(new Pixel(position, colour));
                        intIdPositions[position] = intId;
                    }
                    break;
                }
                case 6: //Incoming pixel someone else sent
                {
                    var i = 1;
                    while (i < msg.Binary.Length)
                    {
                        var index = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan(i)); i += 4;
                        var colour = msg.Binary[i++];
                        BoardSetPixel(new Pixel(index, colour));
                    }
                    break;
                }
                case 7: // Rejected pixel
                {
                    var index = BinaryPrimitives.ReadUInt32BigEndian(data[1..]);
                    var colour = data[5]; // Ignore - We can just unset the pixel to save perf
                    BoardUnsetPixel((int) index);
                    break;
                }
                case 8: // Canvas restriction
                {
                    var locked = msg.Binary[1];
                    var reason = Encoding.UTF8.GetString(msg.Binary[2..]);
                    var lockedNotification = App.Current.Services.GetRequiredService<NotificationStateInfoViewModel>();
                    AddStateInfo(lockedNotification);
                    lockedNotification.PersistsFor = TimeSpan.FromSeconds(60);
                    if (locked == 1)
                    {
                        lockedNotification.Notification = string.IsNullOrEmpty(reason)
                            ? "⚠ Canvas locked: " + reason
                            : "⚠ Warning: Server has notified that this canvas is locked. Edits can not be made.";
                    }
                    else
                    {
                        lockedNotification.Notification = string.IsNullOrEmpty(reason)
                            ? "⚠ Canvas unlocked: " + reason
                            : "⚠ Warning: Canvas unlocked by server. Edits can now be made.";
                    }
                    break;
                }
                case 11:  // Client int ID
                {
                    intId = BinaryPrimitives.ReadUInt32BigEndian(data[1..]);
                    break;
                }
                case 12: // Name info
                {
                    var i = 1;
                    while (i < msg.Binary.Length)
                    {
                        var pIntId = BinaryPrimitives.ReadUInt32BigEndian(data[i..]); i += 4;
                        var pNameLen = data[i++];
                        var pName = Encoding.UTF8.GetString(data[i..(i += pNameLen)]);

                        intIdNames[pIntId] = pName;
                        // Occurs either if server has sent us name it has remembered from a previous session,
                        // or we have just sent server packet 12 name update, and it is sending us back our name
                        if (pIntId == intId)
                        {
                            CurrentPreset.ChatUsername = pName;
                            var nameNotification = App.Current.Services.GetRequiredService<NotificationStateInfoViewModel>();
                            nameNotification.PersistsFor = TimeSpan.FromSeconds(3);
                            nameNotification.Notification = $"Current username set to: {pName} by server";
                            AddStateInfo(nameNotification);
                        }
                    }

                    break;
                }
                case 15: // 15 = chat
                {
                    var offset = 1;
                    var msgType = msg.Binary[offset++];
                    var messageId = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                    offset += 4;
                    var txtLength = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                    offset += 2;
                    var txt = Encoding.UTF8.GetString(msg.Binary, offset, txtLength);
                    offset += txtLength;
                    var senderIntId = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                    offset += 4; // sender int ID
                    var name = intIdNames.GetValueOrDefault(senderIntId) ?? "#" + senderIntId;

                    if (msgType == 0) // live
                    {
                        var liveChatMessage = new LiveChatMessage
                        {
                            MessageId = messageId,
                            Message = txt,
                            Name = name,
                            SenderIntId = senderIntId
                        };
                        
                        var sendDate = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                        offset += 4;
                        liveChatMessage.SendDate = DateTimeOffset.FromUnixTimeSeconds(sendDate);
                        
                        var reactionsL = msg.Binary[offset++];
                        // TODO: Handle reactions
                        
                        var channelL = msg.Binary[offset++];
                        var channelCode = Encoding.UTF8.GetString(msg.Binary, offset, channelL);
                        offset += channelL;
                        var channelViewModel = liveChatVm.Channels
                            .FirstOrDefault(channel => channel.ChannelCode == channelCode);
                        
                        if (msg.Binary.Length - offset >= 4)
                        {
                            var repliesId = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                            liveChatMessage.RepliesTo =
                                channelViewModel?.Messages.FirstOrDefault(message => message.MessageId == repliesId);
                        }
                        
                        if (channelViewModel is null)
                        {
                            channelViewModel = new LiveChatChannelViewModel(channelCode);
                            liveChatVm.Channels.Add(channelViewModel);
                        }

                        if (channelViewModel.Messages.Count > 100)
                        {
                            channelViewModel.Messages.RemoveAt(0);
                        }
                        
                        channelViewModel.Messages.Add(liveChatMessage);
                    }
                    else
                    {
                        var msgPos = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                        if (txt.Length > 56)
                        {
                            txt = txt[..56];
                        }
                    }
                    break;
                }
            }
        });
        Socket.DisconnectionHappened.Subscribe(info =>
        {
            var disconnectNotification = App.Current.Services.GetRequiredService<NotificationStateInfoViewModel>();
            disconnectNotification.PersistsFor = TimeSpan.FromSeconds(20);
            disconnectNotification.Notification = "Disconnected from server... Modtools may need a restart to continue.";
            AddStateInfo(disconnectNotification);
            Console.WriteLine("Disconnected from server: {0}", info.Exception);
        });
        
        await Socket.Start();
    }

    // Decompress changes so it can be put onto canvas
    private byte[] RunLengthChanges(Span<byte> data)
    {
        var i = 0;
        var changeI = 0;
        var changes = new byte[(int) (CanvasWidth * CanvasHeight)];
        // 255 specifies a transparent (blank) pixel (so that you can see through to board layer)
        Array.Fill(changes, (byte) 255);
        
        while (i < data.Length)
        {
            var cell = data[i++];
            // Two MSB used for cell (colour) repeats
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
            changes[changeI++] = (byte) (cell & 63);
        }
        
        return changes;
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
    private void CancelGithubCodeAuth()
    {
        GithubCodePanelVisible = false;
        githubCodeAuthCancelled = true;
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
    private async Task DownloadCanvasPreview()
    {
        if (CurrentBackup is null)
        {
            // TODO: Make a notification state info to say what happened
            return;
        }
        var savePath = await ShowSaveFileDialog(CurrentBackup + "_preview.png", "Download place preview image to filesystem");
        if (savePath is null)
        {
            return;
        }
        var backupPath = UriCombine(CurrentPreset.FileServer, CurrentPreset.BackupsPath, CurrentBackup);
        var placeImg = await CreateCanvasPreviewImage(backupPath);
        placeImg?.Save(savePath);
    }
    
    // TODO: This is bad practice. Find a better way later
    private async Task<string?> ShowSaveFileDialog(string fileName, string title)
    {
        var dialog = new SaveFileDialog
        {
            InitialFileName = fileName,
            Title = title
        };
        
        var mainWindowView = App.Current.Services.GetRequiredService<MainWindow>();
        return await dialog.ShowAsync(mainWindowView);
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
        	Console.WriteLine("ERROR: Selected region is larger than maximum allowed rollback size");
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
        
        var boardResponse = await client.GetAsync(backupUri);
        if (!boardResponse.IsSuccessStatusCode)
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
        
        var backupResponse = await client.GetAsync(backupUri);
        backupResponse.EnsureSuccessStatusCode();
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
    
    [GeneratedRegex(@"github.com\/[\w\-]+\/([\w\-]+)")]
    private static partial Regex RepositoryNameRegex();
}
