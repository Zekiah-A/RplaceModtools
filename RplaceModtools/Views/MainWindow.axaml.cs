using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel.__Internals;
using DynamicData;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.Models;
using SkiaSharp;
using RplaceModtools.ViewModels;
using Websocket.Client;
using Timer = System.Timers.Timer;

namespace RplaceModtools.Views;
public partial class MainWindow : Window
{
    private byte[]? mainChanges;
    private byte[]? mainBoard;

    private bool mouseDown;
    private Point mouseLast;
    private Point mouseTravel;
    private Point lookingAtPixel;
    private readonly HttpClient client = new();
    private readonly Cursor arrow = new (StandardCursorType.Arrow);
    private readonly Cursor cross = new(StandardCursorType.Cross);
    private WebsocketClient? socket;
    private TaskCompletionSource<byte[]> boardFetchSource = new();
    private MainWindowViewModel viewModel;
    
    // TODO: Switch these to using dependency injection
    private PaletteViewModel PVM => (PaletteViewModel) PaletteListBox.DataContext!;
    private LiveChatViewModel LCVM => (LiveChatViewModel) LiveChatGridContainer.DataContext!;
    
    private Point LookingAtPixel
    {
        get
        {
            lookingAtPixel = new Point(
                Math.Clamp(Math.Floor(MainGrid.ColumnDefinitions[0].ActualWidth / 2 - Board.Left), 0, Board.CanvasWidth),
                Math.Clamp(Math.Floor(Height / 2 - Board.Top), 0, Board.CanvasHeight)
            );
            return lookingAtPixel;
        }

        set
        {
            lookingAtPixel = value;
            Board.Left = (float) Math.Floor(MainGrid.ColumnDefinitions[0].ActualWidth / 2 - lookingAtPixel.X);
            Board.Top = (float) Math.Floor(Height / 2 - lookingAtPixel.Y);
        }
    }

    private Point MouseOverPixel(PointerEventArgs e) => new(
        (int) Math.Clamp(Math.Floor(e.GetPosition(CanvasBackground).X - Board.Left * Board.Zoom), 0, Board.CanvasWidth),
        (int) Math.Clamp(Math.Floor(e.GetPosition(CanvasBackground).Y - Board.Top * Board.Zoom), 0, Board.CanvasHeight)
    );

    public MainWindow()
    {
        viewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = viewModel;
        
        InitializeComponent();
        
        CanvasBackground.AddHandler(PointerPressedEvent, OnBackgroundMouseDown, handledEventsToo: false);
        CanvasBackground.AddHandler(PointerMovedEvent, OnBackgroundMouseMove, handledEventsToo: false);
        CanvasBackground.AddHandler(PointerReleasedEvent, OnBackgroundMouseRelease, handledEventsToo: false);
        CanvasBackground.PointerWheelChanged += OnBackgroundWheelChanged;
        
        var windowSize = this.GetObservable(ClientSizeProperty);
        windowSize.Subscribe(size =>
        {
            if (size.Width > 500)
            {
                if (ToolsPanel.Classes.Contains("ToolsPanelClose")) return;
                ToolsPanel.Classes.Add("ToolsPanelClose");
                ToolsPanel.Classes.Remove("ToolsPanelOpen");
            }
            else
            {
                if (ToolsPanel.Classes.Contains("ToolsPanelOpen")) return;
                ToolsPanel.Classes.Add("ToolsPanelOpen");
                ToolsPanel.Classes.Remove("ToolsPanelClose");
            }
        });
    }
    
    //Decompress changes so it can be put onto canv
    private byte[] RunLengthChanges(Span<byte> data)
    {
        // TODO: Fix possible misalignment causing messed up changes
        var i = 0;
        var changeI = 0;
        var changes = new byte[(int) (Board.CanvasWidth * Board.CanvasHeight)];

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
        socket = new WebsocketClient(uri, factory);
        socket.ReconnectTimeout = TimeSpan.FromSeconds(10);
        
        socket.ReconnectionHappened.Subscribe(info =>
        {
            Console.WriteLine("Reconnected to {0}, {1}", uri, info.Type);
        });

        socket.MessageReceived.Subscribe(msg =>
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
                        viewModel.StateInfo.Add(App.Current.Services.GetRequiredService<LiveCanvasStateInfoViewModel>());
                    }

                    // New server packs canvas width and height in code 1, making it 17, equivalent to packet 2
                    if (msg.Binary.Length == 17)
                    {
                        Board.CanvasWidth = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[9..]);
                        Board.CanvasHeight = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[13..]);
                        
                        Task.Run(async () =>
                        {
                            Board.Board = mainBoard = BoardPacker.RunLengthDecompressBoard(
                                await boardFetchSource.Task, (int)(Board.CanvasWidth * Board.CanvasHeight));
                        });
                    }
                    break;
                }
                case 2:
                {
                    // Old board changes packet (contains board info and changes since fetched place file)
                    Board.CanvasWidth = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[1..]);
                    Board.CanvasHeight = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[5..]);
                    Task.Run(async () =>
                    {
                        Board.Board = mainBoard = await boardFetchSource.Task;
                        Board.Changes = mainChanges = RunLengthChanges(msg.Binary.AsSpan()[9..]);
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
                        Board.Set(new Pixel(index, colour));
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
                        Dispatcher.UIThread.Post(() =>
                        {
                            var channelViewModel = LCVM.Channels.FirstOrDefault(channel => channel.ChannelName == channelName);

                            if (channelViewModel is null)
                            {
                                channelViewModel = new LiveChatChannelViewModel(channelName);
                                LCVM.Channels.Add(channelViewModel);
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
                        Console.WriteLine($"Could not handle message: [{msgData[1]}/{msgData[2]}{(uid == null ? "" : "/" + uid)}] {msgData[0]}");
                    }
                    break;
                }
            }
        });
        socket.DisconnectionHappened.Subscribe(info => Console.WriteLine("Disconnected from {0}, {1}", uri, info.Exception));
        
        await socket.Start();
    }

    private void RollbackArea(int x, int y, int regionWidth, int regionHeight, byte[] rollbackBoard)
    {
        // TODO: This check is very very flawed
        if (regionWidth > 250 || regionHeight > 250 || x >= Board.CanvasWidth || y >= Board.CanvasHeight)
        {
            return;
        }
        
        var buffer = new byte[7 + Board.CanvasWidth * Board.CanvasHeight];
        var i = (int) (x + y * Board.CanvasWidth);
        
        // Setting up first 7 bytes of metadata
        new byte[]
        {
            99, (byte) regionWidth, (byte) regionHeight, (byte) (i >> 24), (byte) (i >> 16), (byte) (i >> 8), (byte) i
        }.CopyTo(buffer, 0);
        
        // Copy over just that region of the board into the buffer we send to server
        for (var row = 0; row < regionHeight; row++)
        {
            Buffer.BlockCopy(rollbackBoard, i, buffer, row * regionWidth + 7, i + regionWidth);
            i += (int) Board.CanvasWidth;
        }

        if (socket is { IsRunning: true })
        {
            socket.Send(buffer);
        }
    }
    
    private async Task FetchCacheBackupList()
    {
        if (viewModel.CurrentPreset.LegacyServer)
        {
            var nameMatches = RepositoryNameRegex().Match(viewModel.CurrentPreset.BackupsRepository).Groups.Values.First();
            var invalidChars = new string(Path.GetInvalidFileNameChars());
            var safeRepositoryName = Regex.Replace(nameMatches.Value, $"[{Regex.Escape(invalidChars)}]", "_");
            
            var localPath = Path.Join(MainWindowViewModel.ProgramDirectory, safeRepositoryName);
            if (!Directory.Exists(localPath))
            {
                Repository.Clone(viewModel.CurrentPreset.BackupsRepository, localPath, new CloneOptions
                {
                    BranchName = "main",
                });
            }
            
            using var backupsRepository = new Repository(localPath);
            var signature = new Signature("RplaceModtools", "rplacemodtools@unknown.com", DateTimeOffset.Now);
            var mergeResult = Commands.Pull(backupsRepository, signature, new PullOptions());

            if (mergeResult.Status == MergeStatus.Conflicts)
            {
                Console.WriteLine($"Merge failed - serious file conflict. Try deleting local backups repository? ${localPath}");
            }

            var placePath = viewModel.CurrentPreset.PlacePath.StartsWith("/") 
                ? viewModel.CurrentPreset.PlacePath[1..]
                : viewModel.CurrentPreset.PlacePath;
            var commits = backupsRepository.Commits.QueryBy(placePath)
                .Select(commit => commit.Commit.Id.Sha).ToList();
            
            viewModel.Backups = new ObservableCollection<string> { "place" };
            viewModel.Backups.AddRange(commits);
        }
        else
        {
            var backupsUri = UriCombine(viewModel.CurrentPreset.FileServer!, viewModel.CurrentPreset.BackupListPath);
            var response = await Fetch(backupsUri);
            viewModel.Backups = new ObservableCollection<string> { "place" };
            if (response is null)
            {
                return;
            }
        
            var responseBody = await response.Content.ReadAsStringAsync();
            viewModel.Backups.AddRange(responseBody.Split("\n"));
        }
    }
    
    private async Task<HttpResponseMessage?> Fetch(Uri uri)
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

    private async Task<Bitmap?> CreateCanvasPreviewImage<T>(T input)
    {
        async Task<byte[]?> FetchBoardAsync(Uri uri)
        {
            var boardResponse = await Fetch(uri);
            if (boardResponse is not null)
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
        
        // TODO: Board.CanvasWidth, (int)Board.CanvasHeight could be wrong, we need to ensure board is properly unpacked
        var imageInfo = new SKImageInfo((int)Board.CanvasWidth, (int)Board.CanvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        Parallel.For(0, placeFile.Length, i =>
        {
            var x = (int) (i % Board.CanvasWidth);
            var y = (int) (i / Board.CanvasWidth);
            canvas.DrawPoint(x, y, PaletteViewModel.Colours.ElementAtOrDefault(placeFile[i]));
        });
        
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data is null) // TODO: Figure out why when using bitmap we get null data
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

    //App started
    private async void OnStartButtonPressed(object? sender, RoutedEventArgs e)
    {
        //Configure the current session's data
        if (!MainWindowViewModel.ServerPresetExists(viewModel.CurrentPreset))
        {
            MainWindowViewModel.SaveServerPreset(viewModel.CurrentPreset);
        }
        
        //UI and connections
        _ = CreateConnection(UriCombine(viewModel.CurrentPreset.Websocket, viewModel.CurrentPreset.AdminKey));
        _ = Task.Run(async () =>
        {
            var boardPath = UriCombine(viewModel.CurrentPreset.FileServer, viewModel.CurrentPreset.PlacePath);
            var boardResponse = await Fetch(boardPath);
            if (boardResponse is null)
            {
                throw new Exception("Initial board load failed. Board response was null");
            }
            
            boardFetchSource.SetResult(await boardResponse.Content.ReadAsByteArrayAsync());
            //PreviewImg.Source = await CreateCanvasPreviewImage(boardResponse);
        });
        PlaceConfigPanel.IsVisible = false;
        DownloadBtn.IsEnabled = true;
        
        await FetchCacheBackupList();
        CanvasDropdown.SelectedIndex = 0;
        BackupCheckInterval();
    }
    
    private void OnBackgroundMouseDown(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is null || !e.Source.Equals(CanvasBackground)) return; //stop bubbling
        mouseTravel = new Point(0, 0);
        mouseDown = true;
        
        if (viewModel.CurrentTool == Tool.Select)
        {
            var mousePos = MouseOverPixel(e);
            Board.StartSelection(mousePos, mousePos);
        }
    }

    private int BoardColourAt(uint x, uint y)
    {
        var index = (int)(x + y * Board.CanvasWidth);
        var socketColour = Board.SocketPixels?.ElementAtOrDefault(index);
        if (socketColour != null && socketColour != 255)
        {
            return (byte)socketColour;
        }

        var changesColour = Board.Changes?.ElementAt(index);
        if (changesColour is not null && changesColour != 255)
        {
            return (byte)changesColour;
        }

        return Board.Board?.ElementAt(index) ?? -1;
    }

    private void OnBackgroundMouseMove(object? sender, PointerEventArgs e)
    {
        if (e.Source is null || !e.Source.Equals(CanvasBackground)) return;
        if (mouseDown)
        {
            //If left mouse button, go to colour picker mode from the canvas instead
            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                if (e.GetPosition(CanvasBackground).X - Board.Left < 0 ||
                    e.GetPosition(CanvasBackground).X - Board.Left > Board.CanvasWidth * Board.Zoom ||
                    e.GetPosition(CanvasBackground).Y - Board.Left < 0 ||
                    e.GetPosition(CanvasBackground).Y - Board.Left > Board.CanvasHeight * Board.Zoom)
                {
                    return;
                }
                
                CursorIndicatorRectangle.IsVisible = true;
                Canvas.SetLeft(CursorIndicatorRectangle, e.GetPosition(CanvasBackground).X + 8);
                Canvas.SetTop(CursorIndicatorRectangle, e.GetPosition(CanvasBackground).Y + 8);
                Cursor = cross;
                
                //If mouse if over board, then get pixel colour at that position.
                //var pixelColour =  
                var colourIndex = BoardColourAt((uint)Math.Floor(e.GetPosition(CanvasBackground).X),
                    (uint)Math.Floor(e.GetPosition(CanvasBackground).Y));
                if (colourIndex == -1)
                {
                    return;
                }
                
                var pixelColour = PaletteViewModel.Colours[colourIndex];
                PVM.CurrentColour = (byte) colourIndex;
                CursorIndicatorRectangle.Background = new SolidColorBrush(
                    new Color(pixelColour.Alpha, pixelColour.Red, pixelColour.Green, pixelColour.Blue));
                return;
            }
            if (viewModel.CurrentTool == Tool.Select)
            {
                Board.UpdateSelection(null, MouseOverPixel(e));
                return;
            }
            if (PVM.CurrentColour is not null) //drag place pixels
            {
                var mousePos = MouseOverPixel(e);
                var pixel = new Pixel((int)Math.Floor(mousePos.X), (int)Math.Floor(mousePos.Y), Board.CanvasWidth, PVM.CurrentColour ?? 0);
                SetPixels(pixel, viewModel.CurrentPaintBrushRadius);
                return;
            }
            
            //Multiply be 1/zoom so that it always moves at a speed to make it seem to drag with the mouse regardless of zoom level
            Board.Left += (float) (e.GetPosition(CanvasBackground).X - mouseLast.X) * (1 / Board.Zoom);
            Board.Top += (float) (e.GetPosition(CanvasBackground).Y - mouseLast.Y) * (1 / Board.Zoom);
            
            //Clamp values
            Board.Left = (float)Math.Clamp(Board.Left,
                MainGrid.ColumnDefinitions[0].ActualWidth / 2 - Board.CanvasWidth * Board.Zoom,
                MainGrid.ColumnDefinitions[0].ActualWidth / 2);
            Board.Top = (float) Math.Clamp(Board.Top, Height / 2 - (Board.CanvasHeight) * Board.Zoom, Height / 2);
        }
        else
        {
            Cursor = arrow;
            CursorIndicatorRectangle.IsVisible = false;
        }
        mouseTravel += new Point(Math.Abs(e.GetPosition(CanvasBackground).X - mouseLast.X), Math.Abs(e.GetPosition(CanvasBackground).Y - mouseLast.Y));
        mouseLast = e.GetPosition(CanvasBackground);
    }
    
    private void OnBackgroundMouseRelease(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source is null || !e.Source.Equals(CanvasBackground))
        {
            return;
        }
        mouseDown = false;
        
        //click place pixel
        if (PVM.CurrentColour is null) return;
        var mousePos = MouseOverPixel(e);
        var pixel = new Pixel((int)Math.Floor(mousePos.X), (int)Math.Floor(mousePos.Y), Board.CanvasWidth, PVM.CurrentColour ?? 0);
        SetPixels(pixel, viewModel.CurrentPaintBrushRadius);
        
        PVM.CurrentColour = null;
    }

    //https://github.com/rslashplace2/rslashplace2.github.io/blob/1cc30a12f35a6b0938e538100d3337228087d40d/index.html#L531
    private void OnBackgroundWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        //Board.Left -= (float) (e.GetPosition(this).X - MainGrid.ColumnDefinitions[0].ActualWidth / 2) / 50;
        //Board.Top -= (float) (e.GetPosition(this).Y - Height / 2) / 50;
        //LookingAtPixel = new Point((float) e.GetPosition(Board).X * Board.Zoom, (float) e.GetPosition(Board).Y * Board.Zoom);
        var pos = LookingAtPixel;
        Board.Zoom += (float) e.Delta.Y / 10;
        LookingAtPixel = pos;
    }

    // HACK: Selection changed fires for no reason
    private string previousBackup = "";
    
    private void OnCanvasDropdownSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (viewModel.CurrentBackup is null || viewModel.CurrentBackup == previousBackup)
        {
            return;
        }
        previousBackup = viewModel.CurrentBackup;
        
        Task.Run(async () =>
        {
            if (viewModel.CurrentBackup == "place")
            {
                await ViewMainCanvas();
            }
            else if (viewModel.ViewSelectedBackupArea)
            {
                await ViewCanvasBackupSelection(viewModel.CurrentBackup);
            }
            else
            {
                await ViewCanvasAtBackup(viewModel.CurrentBackup);
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
        if (mainBoard is null)
        {
            throw new Exception("Can not view main canvas. Main board from initial fetch was null.");
        }

        Board.Board = mainBoard;
        Board.Changes = mainChanges;
        //PreviewImg.Source = await CreateCanvasPreviewImage(mainBoard) ?? new Bitmap("../Assets/preview_default.png");
    }

    /// <summary>
    /// Views the whole canvas at a certain date/backup. Unlike ViewCanvasSelectionBackup there is no portalling at all
    /// through the current selection.
    /// </summary>
    private async Task ViewCanvasAtBackup(string backupName)
    {
        var backupUri = viewModel.CurrentPreset.LegacyServer ?
            UriCombine(viewModel.CurrentPreset.FileServer, backupName, viewModel.CurrentPreset.PlacePath)
            : UriCombine(viewModel.CurrentPreset.FileServer, viewModel.CurrentPreset.BackupsPath, backupName);
        
        var boardResponse = await Fetch(backupUri);
        if (boardResponse is not null)
        {
            // TODO: Otherwise log that this has gone horrifically wrong
            Board.Board = await boardResponse.Content.ReadAsByteArrayAsync();
        }
        
        Board.Changes = null;
        Board.ClearSelections();
        
        viewModel.StateInfo.Add(App.Current.Services.GetRequiredService<LockedCanvasStateInfoViewModel>());
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
        
        var backupUri = viewModel.CurrentPreset.LegacyServer ?
            // TODO: Remove hard code for this and make it part of the preset, something like git raw path
            UriCombine(viewModel.CurrentPreset.FileServer, backupName, viewModel.CurrentPreset.PlacePath)
            : UriCombine(viewModel.CurrentPreset.FileServer, viewModel.CurrentPreset.BackupsPath, backupName);
        
        Board.ClearSelections();
        var backupResponse = await Fetch(backupUri);
        var backupTask = backupResponse?.Content.ReadAsByteArrayAsync();
        if (backupTask is not null)
        {
            Board.SelectionBoard = await backupTask;
        }
    }

    private void OnViewSelectedBackupChecked(object? sender, RoutedEventArgs e)
    {
        var backupName = CanvasDropdown.SelectedItem as string ?? "place";
        Task.Run(() => ViewCanvasAtBackup(backupName));
    }

    private void OnViewSelectedBackupUnchecked(object? sender, RoutedEventArgs e)
    {
        var backupName = CanvasDropdown.SelectedItem as string ?? viewModel.CurrentPreset.PlacePath;
        Task.Run(() => ViewCanvasBackupSelection(backupName));
    }
    
    private void OnResetCanvasViewPressed(object? sender, RoutedEventArgs e)
    {
        Board.Left = 0;
        Board.Top = 0;
        Board.Zoom = 1;
    }

    private void OnSelectColourClicked(object? sender, RoutedEventArgs e)
    {
        Palette.IsVisible = true;
    }

    private void OnPaletteDoneButtonClicked(object? sender, RoutedEventArgs e)
    {
        Palette.IsVisible = false;
    }

    private void OnPaletteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        PVM.CurrentColour = (byte) ((sender as ListBox)?.SelectedIndex ?? PVM.CurrentColour ?? 0);
    }
    
    private async void OnDownloadPreviewPressed(object? sender, RoutedEventArgs e)
    {
        var savePath = await ShowSaveFileDialog(
            (CanvasDropdown.SelectedItem as string ?? "place") + "_preview.png", "Download place preview image to filesystem");
        if (savePath is null)
        {
            return;
        }
        
        var backupPath = UriCombine(viewModel.CurrentPreset.FileServer, viewModel.CurrentPreset.BackupsPath,
            CanvasDropdown.SelectedItem! as string ?? "place");
        var placeImg = await CreateCanvasPreviewImage(backupPath);
        placeImg?.Save(savePath);
    } 

    private async Task<string?> ShowSaveFileDialog(string fileName, string title)
    {
        var dialog = new SaveFileDialog
        {
            InitialFileName = fileName,
            Title = title
        };
        
        return await dialog.ShowAsync(this);
    }
    
    private void SetPixels(Pixel px, int radius)
    {
        if (radius == 1)
        {
            Board.Set(px);
            if (socket is {IsRunning: true}) socket.Send(px.ToByteArray());
            return;
        }

        var brushStack = new Stack<Pixel>();
        var realPos = px.GetPosition(Board.CanvasWidth);
        if (viewModel.CurrentBrushShape == Shape.Square)
        {
            var diameter = 2 * radius + 1;

            for (var h = 0; h < diameter; h++)
            {
                for (var v = 0; v < diameter; v++)
                {
                    // x, y relative to circle coordinates
                    var x = h - radius;
                    var y = v - radius;

                    if (x * x + y * y > radius * radius + 1)
                    {
                        continue;
                    }

                    var pixelX = (int) (radius + x + realPos.X - radius / 2);
                    var pixelY = (int) (radius + y + realPos.Y - radius / 2);
                    if (pixelX < 0 || pixelX > Board.CanvasWidth || pixelY < 0 || pixelY > Board.CanvasHeight)
                    {
                        continue;
                    }

                    var radiusPx = px.Clone();
                    radiusPx.SetPosition(pixelX, pixelY, Board.CanvasWidth);
                    Board.Set(radiusPx);
                    brushStack.Push(radiusPx);
                }
            }
        }
        else
        {
            for (var x = 0 - radius / 2; x < radius / 2; x++)
            {
                for (var y = 0 - radius / 2; y < radius / 2; y++)
                {
                    var radiusPx = px.Clone();
                    radiusPx.SetPosition(x, y, Board.CanvasWidth);
                    Board.Set(radiusPx);
                    brushStack.Push(radiusPx);
                }
            }
        }
        
        Task.Run(() =>
        {
            while (brushStack.Count > 0)
            {
                Task.Delay(30);
                if (socket is { IsRunning: true })
                {
                    socket.Send(brushStack.Pop().ToByteArray());
                }
            }
        });
    }

    private void OnToggleThemePressed(object? sender, RoutedEventArgs e)
    {
        // TODO: Fix light/dark theme
        //var currentStyle = (FluentTheme) Application.Current?.Styles[0]!;
        //currentStyle.Mode = currentStyle.Mode == FluentThemeMode.Dark ? FluentThemeMode.Light : FluentThemeMode.Dark;
    }

    private void OnOpenGithubClicked(object? sender, RoutedEventArgs e)
    {
        string? processName = null;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            processName = "xdg-open";
        else if (OperatingSystem.IsWindows())
            processName = "";
        else if (OperatingSystem.IsMacOS())
            processName = "open";
        if (processName is null) return;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = processName,
                Arguments = "https://github.com/Zekiah-A/rplace-modtools"
            }
        };
        
        process.Start();
    }

    private void OnRollbackButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (Board.SelectionBoard is null)
        {
            return;
        }
        
        var sel = Board.Selections.Peek();
        RollbackArea((int) sel.TopLeft.X, (int) sel.TopLeft.Y, (int) sel.BottomRight.X - (int) sel.TopLeft.X, 
            (int) sel.BottomRight.Y - (int) sel.TopLeft.Y, Board.SelectionBoard);
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

    private void ToolToggleButtonCheck(object? sender, RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(sender);
        var toggleButton = (ToggleButton) sender;
        
        switch (toggleButton.Name)
        {
            case "PaintTool":
                RubberTool.IsChecked = false;
                SelectTool.IsChecked = false;
                break;
            case "RubberTool":
                PaintTool.IsChecked = false;
                SelectTool.IsChecked = false;
                break;
            case "SelectTool":
                RubberTool.IsChecked = false;
                PaintTool.IsChecked = false;
                break;
        }
    }

    private void OnPresetsAdvancedClicked(object? sender, RoutedEventArgs e)
    {
        PresetsAdvancedPanel.IsVisible = !PresetsAdvancedPanel.IsVisible;
        PresetsAdvancedButton.Content = "Advanced " +  (PresetsAdvancedPanel.IsVisible ? "▲" : "▼");
    }

    private static Uri UriCombine(params string[] parts)
    {
        return new Uri(string.Join("/", parts
            .Where(part => !string.IsNullOrEmpty(part))
            .Select(subPath => subPath.Trim('/'))
            .ToArray()));
    }

    private void OnChatSendPressed(object? sender, RoutedEventArgs e)
    {
        SendChatInputMessage(ChatInput.Text);
        ChatInput.Text = "";
    }
    
    private void OnChatInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SendChatInputMessage(ChatInput.Text); 
            ChatInput.Text = "";
        }
    }

    private void SendChatInputMessage(string input)
    {
        if (input.StartsWith(":help"))
        {
            LCVM.CurrentChannel.Messages.Add(new ChatMessage
            {
                Name = "!!",
                Message = "Commands:\n :help, displays commands,\n`:name username` sets your livechat username"
            });
        }
        else if (input.StartsWith(":name"))
        {
            viewModel.ChatUsername = ChatInput.Text[5..].Trim();
            
            LCVM.CurrentChannel.Messages.Add(new ChatMessage
            {
                Name = "!!",
                Message = "Chat username set to " + viewModel.ChatUsername
            });
        }
        else if (viewModel.ChatUsername is null)
        {
            LCVM.CurrentChannel.Messages.Add(new ChatMessage
            {
                Name = "!!",
                Message = "No chat username set! Use command `:name username`, and use :help for more commands"
            });
        }
        else
        {
            var chatBuilder = new StringBuilder();
            chatBuilder.AppendLine(input);
            chatBuilder.AppendLine(viewModel.ChatUsername);
            chatBuilder.AppendLine(LCVM.CurrentChannel.ChannelName);
            chatBuilder.AppendLine("live");
            chatBuilder.AppendLine("0");
            chatBuilder.AppendLine("0");
            socket?.Send(Encoding.UTF8.GetBytes("\x0f" + chatBuilder));
        }
    }
    
    private void OnModerateUserPressed(object? sender, PointerPressedEventArgs e)
    {
        
        ActionConfigPanel.IsVisible = true;
        viewModel.CurrentModerationAction = ModerationAction.None;
        viewModel.CurrentModerationUid = "";
    }

    private void OnKickChatterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ChatMessage message })
        {
            return;
        }
        
        ActionConfigPanel.IsVisible = true;
        viewModel.CurrentModerationAction = ModerationAction.Kick;
        viewModel.CurrentModerationUid = message.Uid;
    }
    
    private void OnMuteChatterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ChatMessage message })
        {
            return;
        }
        
        ActionConfigPanel.IsVisible = true;
        viewModel.CurrentModerationAction = ModerationAction.Mute;
        viewModel.CurrentModerationUid = message.Uid;
    }

    private void OnBanChatterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: ChatMessage message })
        {
            return;
        }
        
        ActionConfigPanel.IsVisible = true;
        viewModel.CurrentModerationAction = ModerationAction.Ban;
        viewModel.CurrentModerationUid = message.Uid;
    }

    private void OnActionConfigSubmitPressed(object? sender, RoutedEventArgs e)
    {
        switch (viewModel.CurrentModerationAction)
        {
            case ModerationAction.Kick:
            {
                var reasonBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationReason);
                var uidBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationUid);
                var kickBuffer = new byte[3 + uidBytes.Length + reasonBytes.Length];
                kickBuffer[0] = 98;
                kickBuffer[1] = (byte) ModerationAction.Kick;
                kickBuffer[2] = (byte) uidBytes.Length;
                uidBytes.CopyTo(kickBuffer.AsSpan()[3..]);
                reasonBytes.CopyTo(kickBuffer.AsSpan()[(3 + uidBytes.Length)..]);
                socket?.Send(kickBuffer);
                break;
            }
            case ModerationAction.Mute:
            {
                var reasonBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationReason);
                var uidBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationUid);
                var muteBuffer = new byte[7 + uidBytes.Length + reasonBytes.Length];
                muteBuffer[0] = 98;
                muteBuffer[1] = (byte) ModerationAction.Mute;
                BinaryPrimitives.WriteUInt32BigEndian(muteBuffer.AsSpan()[2..], (ushort) viewModel.CurrentModerationDuration.TotalSeconds);
                muteBuffer[6] = (byte) uidBytes.Length;
                uidBytes.CopyTo(muteBuffer.AsSpan()[7..]);
                reasonBytes.CopyTo(muteBuffer.AsSpan()[(7 + uidBytes.Length)..]);
                socket?.Send(muteBuffer);
                break;
            }
            case ModerationAction.Ban:
            {
                var reasonBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationReason);
                var uidBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationUid);
                var muteBuffer = new byte[7 + uidBytes.Length + reasonBytes.Length];
                muteBuffer[0] = 98;
                muteBuffer[1] = (byte) ModerationAction.Ban;
                BinaryPrimitives.WriteUInt32BigEndian(muteBuffer.AsSpan()[2..], (ushort) viewModel.CurrentModerationDuration.TotalSeconds);
                muteBuffer[6] = (byte) uidBytes.Length;
                uidBytes.CopyTo(muteBuffer.AsSpan()[7..]);
                reasonBytes.CopyTo(muteBuffer.AsSpan()[(7 + uidBytes.Length)..]);
                socket?.Send(muteBuffer);
                break;
            }
            case ModerationAction.Captcha:
            {
                var reasonBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationReason);
                var uidBytes = (byte[]?) null;
                var uidBytesLength = 0;
                if (!viewModel.CurrentModerationAll)
                {
                    uidBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationUid);
                    uidBytesLength = uidBytes.Length;
                }

                var captchaBuffer = new byte[3 + (uidBytes?.Length ?? 0) + reasonBytes.Length];
                captchaBuffer[0] = 98;
                captchaBuffer[1] = (byte) ModerationAction.Captcha;
                captchaBuffer[2] = (byte) uidBytesLength;
                uidBytes?.CopyTo(captchaBuffer, 3);
                reasonBytes.CopyTo(captchaBuffer, 3 + uidBytesLength);
                socket?.Send(captchaBuffer);
                break;
            }
        }
        
        ResetCurrentModerationAction();
    }
    
    private void OnActionConfigCancelPressed(object? sender, RoutedEventArgs e)
    {
        ResetCurrentModerationAction();
    }

    private void ResetCurrentModerationAction()
    {
        ActionConfigPanel.IsVisible = false;
        viewModel.CurrentModerationAction = ModerationAction.None;
        viewModel.CurrentModerationUid = "";
    }

    private void OnLoadLocalClicked(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    [GeneratedRegex(@"github.com\/[\w\-]+\/([\w\-]+)")]
    private static partial Regex RepositoryNameRegex();
}
