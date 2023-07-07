using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.Models;
using SkiaSharp;
using RplaceModtools.ViewModels;
using Websocket.Client;
using Timer = System.Timers.Timer;

namespace RplaceModtools.Views;
public partial class MainWindow : Window
{
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
    //TODO: Switch these to using dependency injection
    private PaletteViewModel PVM => (PaletteViewModel) PaletteListBox.DataContext!;
    private ServerPresetViewModel SPVM => (ServerPresetViewModel) ServerPresetListBox.DataContext!;
    private LiveChatViewModel LCVM => (LiveChatViewModel) LiveChatListBox.DataContext!;
    
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
        (int) Math.Clamp(Math.Floor(e.GetPosition(CanvasBackground).X - Board.Left), 0, Board.CanvasWidth),
        (int) Math.Clamp(Math.Floor(e.GetPosition(CanvasBackground).Y - Board.Top), 0, Board.CanvasHeight)
    );

    public MainWindow()
    {
        viewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = viewModel;
        
        InitializeComponent();
        
        // Setting it from XAML causes the event to be fired before other controls get initialized which throws a NRE.
        PaintTool.IsChecked = true;
        
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
                ToolsPanel.Classes = Classes.Parse("ToolsPanelClose");
            }
            else
            {
                if (ToolsPanel.Classes.Contains("ToolsPanelOpen")) return;
                ToolsPanel.Classes = Classes.Parse("ToolsPanelOpen");
            }
        });
    }
    
    //Decompress changes so it can be put onto canv
    private byte[] RunLengthChanges(byte[] data)
    {
        var changeI = 0;
        var changes = new byte[(int) (Board.CanvasWidth * Board.CanvasHeight)];

        for (var i = 9; i < data.Length;)
        {
            var cell = data[i++];
            var c = cell >> 6;
            switch (c)
            {
                case 1:
                    c = data[i++];
                    break;
                case 2:
                    c = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan()[i++..]);
                    i++;
                    break;
                case 3:
                    c = (int) BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan()[i++..]);
                    i += 3;
                    break;
            }
            changeI += c;
            changes[changeI] = (byte) (cell & 63);
        }
        return changes;
    }

    private async Task CreateConnection(string uri)
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
        socket = new WebsocketClient(new Uri(uri), factory);
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
                    // New server board info packet, superceeds packet 2, also used by old board for cooldown
                    var cooldown = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[5..]);
                    if (cooldown == 0xFFFFFFFF)
                    {
                        Console.WriteLine("Canvas is locked (readonly). Edits can not be made");
                    }

                    // New server packs canvas width and height in code 1, making it 17, equivalent to packet 2
                    if (msg.Binary.Length == 17)
                    {
                        Board.CanvasWidth = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[9..]);
                        Board.CanvasHeight = BinaryPrimitives.ReadUInt32BigEndian(msg.Binary.AsSpan()[13..]);
                        
                        Task.Run(async () =>
                        {
                            Board.Board = BoardPacker.RunLengthDecompressBoard(
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
                        Board.Board = await boardFetchSource.Task;
                    });
                    
                    Board.Changes = RunLengthChanges(msg.Binary);
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
                /*case 7: //Sending back what you placed
                {
                    var pos = BitConverter.ToUInt32(msg.Binary.ToArray(), 5);
                    var col = msg.Binary[9];
                    break;
                }*/
                case 15: // 15 = chat
                {
                    var msgData = Encoding.UTF8.GetString(msg.Binary).Split("\n");
                    var text = msgData.ElementAtOrDefault(0);
                    var name = msgData.ElementAtOrDefault(1);
                    var channel = msgData.ElementAtOrDefault(2);
                    var type = msgData.ElementAtOrDefault(3);
                    var x = msgData.ElementAtOrDefault(4);
                    var y = msgData.ElementAtOrDefault(4);
                    
                    /*LCVM.AddMessage(new ChatMessage
                    {
                        Name = name,
                        Message = text,
                        Channel = channel
                    });*/
                    break;
                }
            }
        });
        socket.DisconnectionHappened.Subscribe(info => Console.WriteLine("Disconnected from {0}, {1}", uri, info.Exception));
        
        await socket.Start();
    }

    /*private void RollbackArea(int x, int y, int w, int h, byte[] rollbackBoard)
    {
        if (w > 250 || h > 250 || x >= Board.CanvasWidth || y >= Board.CanvasHeight) return;
        var buffer = new byte[Board.CanvasWidth ?? 500 * Board.CanvasHeight ?? 500 + 7];
        var i = x + y * Board.CanvasWidth ?? 500;
        new byte[] {99, (byte) w, (byte) h, (byte) (i >> 24), (byte) (i >> 16), (byte) (i >> 8), (byte) i}.CopyTo(buffer, 0);
        
        for (var hi = 0; hi < h; hi++)
        {
            BinaryPrimitives.WriteInt32BigEndian(rollbackBoard.AsSpan().Slice(i, i + w),hi * w + 7);
            rollbackBoard[i..(i + w)].CopyTo(buffer, hi * w + 7);
            i += Board.CanvasWidth ?? 500;
        }
        
        if (socket is {IsRunning: true}) socket.Send(buffer);
    }*/

    private async Task FetchCacheBackupList()
    {
        var backupsUri = UriCombine(viewModel.CurrentPreset.FileServer!, viewModel.CurrentPreset.BackupListPath);
        var response = await Fetch(backupsUri);
        if (response is null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                CanvasDropdown.Items = new[] { "place" };
            });
            return;
        }
        
        var responseBody = await response.Content.ReadAsStringAsync();
        var stack = new Stack(responseBody.Split("\n"));
        stack.Pop();
        stack.Push("place");
        var backupArr = (object[]) stack.ToArray();
        CanvasDropdown.Items = backupArr;
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
        
        using var bmp = new SKBitmap((int)Board.CanvasWidth, (int)Board.CanvasHeight, true);
        for (var i = 0; i < placeFile.Length; i++)
        {
            bmp.SetPixel((int)(i % Board.CanvasWidth), (int)(i / Board.CanvasWidth),
                PaletteViewModel.Colours.ElementAtOrDefault(placeFile[i]));
        }
        
        using var bitmap = bmp.Encode(SKEncodedImageFormat.Png, 100);
        await using var imgStream = new MemoryStream();
        imgStream.Position = 0;
        bitmap.SaveTo(imgStream);
        imgStream.Seek(0, SeekOrigin.Begin);
        bmp.Dispose();
        bitmap.Dispose();
        return new Bitmap(imgStream);
    }

    //App started
    private async void OnStartButtonPressed(object? sender, RoutedEventArgs e)
    {
        //Configure the current session's data
        if (!ServerPresetViewModel.ServerPresetExists(viewModel.CurrentPreset))
        {
            ServerPresetViewModel.SaveServerPreset(viewModel.CurrentPreset);
        }
        
        //UI and connections
        _ = CreateConnection(viewModel.CurrentPreset.Websocket + viewModel.CurrentPreset.AdminKey);
        _ = Task.Run(async () =>
        {
            var boardPath = UriCombine(viewModel.CurrentPreset.FileServer,
                viewModel.CurrentPreset.PlacePath);
            var boardResponse = await Fetch(boardPath);
            if (boardResponse is null)
            {
                // TODO: Log that something has gone fatally wrong to console, close connection and perhaps
                // TODO: allow them to to connect/start again
                throw new Exception("Initial board load failed. Board response was null");
            }
            
            boardFetchSource.SetResult(await boardResponse.Content.ReadAsByteArrayAsync());
        });
        PlaceConfigPanel.IsVisible = false;
        DownloadBtn.IsEnabled = true;

        //Backup loading
        await FetchCacheBackupList();
        CanvasDropdown.SelectedIndex = 0;
        BackupCheckInterval();
    }

    private void OnServerPresetsSelectionChanged(object? sender, SelectionChangedEventArgs args) =>
        viewModel!.CurrentPreset = SPVM.ServerPresets[ServerPresetListBox.SelectedIndex];

    private void OnBackgroundMouseDown(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is null || !e.Source.Equals(CanvasBackground)) return; //stop bubbling
        mouseTravel = new Point(0, 0);
        mouseDown = true;
        if (viewModel!.CurrentTool == Tool.Select) Board.StartSelection(MouseOverPixel(e), MouseOverPixel(e));
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
        //Board.Left -= (float) (e.GetPosition(this).X - MainGrid.ColumnDefinitions[0].ActualWidth / 2) / 50; //Board.Top -= (float) (e.GetPosition(this).Y - Height / 2) / 50;
        LookingAtPixel = new Point((float) e.GetPosition(Board).X, (float) e.GetPosition(Board).Y);
        Board.Zoom += (float) e.Delta.Y / 10;
    }

    private void OnCanvasDropdownSelectionChanged(object? _, SelectionChangedEventArgs e)
    {
        Task.Run(async () =>
        {
            var backupName = CanvasDropdown.SelectedItem as string ?? viewModel.CurrentPreset.PlacePath;
            
            if (ViewSelectedBackup.IsChecked is true)
            {
                await ViewCanvasAtBackup(backupName);
            }
            else
            {
                await ViewCanvasBackupSelection(backupName);
            }
        });
    }

    /// <summary>
    /// Views the whole canvas at a certain date/backup. Unlike ViewCanvasSelectionBackup there is no portalling at all
    /// through the current selection.
    /// </summary>
    private async Task ViewCanvasAtBackup(string backupName)
    {
        var backupUri = UriCombine(viewModel.CurrentPreset.FileServer, viewModel.CurrentPreset.BackupsPath, backupName);
        PreviewImg.Source = await CreateCanvasPreviewImage(backupUri) ?? new Bitmap("../Assets/preview_default.png");
        var boardResponse = await Fetch(backupUri);
        if (boardResponse is not null)
        {
            // TODO: Otherwise log that this has gone horrifically wrong
            Board.Board = await boardResponse.Content.ReadAsByteArrayAsync();
        }
        
        Board.Changes = null;
        Board.ClearSelections();
        viewModel.StateInfo = CanvasDropdown.SelectedIndex != 0 ? App.Current.Services.GetRequiredService<LiveCanvasStateInfoViewModel>() : null;
    }

    /// <summary>
    /// Views the canvas with the board being predominantly at the live/current date, but will also hold the selected  
    /// backup inside of the selections render pass, so that by selecting an area you can see a 'portal' into that older
    /// canvas backup.
    /// </summary>
    private async Task ViewCanvasBackupSelection(string backupName)
    {
        var placePath = UriCombine(viewModel.CurrentPreset.FileServer, viewModel.CurrentPreset.PlacePath);
        PreviewImg.Source = await CreateCanvasPreviewImage(placePath) ?? new Bitmap("../Assets/preview_default.png");

        var boardResponse = await Fetch(placePath);
        if (boardResponse is not null)
        {
            // TODO: Otherwise log that this has gone horrifically wrong
            Board.Board = await boardResponse.Content.ReadAsByteArrayAsync(); 
        }
        
        var backupUri = UriCombine(viewModel.CurrentPreset.FileServer, viewModel.CurrentPreset.BackupsPath, backupName);
        Board.ClearSelections();
        var backupResponse = await Fetch(backupUri);
        if (boardResponse is not null)
        {
            // TODO: Otherwise log that this has gone horrifically wrong
            Board.SelectionBoard = await backupResponse!.Content.ReadAsByteArrayAsync();
        }
        viewModel.StateInfo = null;
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
        if (viewModel.CurrentBrushShape == Shape.Square)
        {
            var diameter = 2 * radius + 1;
            for (var i = 0; i < diameter; i++)
            {
                for (var j = 0; j < diameter; j++)
                {
                    var y = j - radius;
                    var x = i - radius;

                    if (x * x + y * y > radius * radius + 1)
                    {
                        continue;
                    }
                    
                    var radiusPx = px.Clone();
                    radiusPx.SetPosition(x, y, Board.CanvasWidth);
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
        var currentStyle = (FluentTheme) Application.Current?.Styles[0]!;
        currentStyle.Mode = currentStyle.Mode == FluentThemeMode.Dark ? FluentThemeMode.Light : FluentThemeMode.Dark;
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
        if (Board.SelectionBoard is null) return;
        var sel = Board.Selections.Peek();
        //RollbackArea((int) sel.TopLeft.X, (int) sel.TopLeft.Y, (int) sel.BottomRight.X - (int) sel.TopLeft.X,
        //(int) sel.BottomRight.Y - (int) sel.TopLeft.Y, Board.SelectionBoard);
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
}
