using System.Collections;
using System.Net.WebSockets;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using DynamicData;
using rPlace.Models;
using SkiaSharp;
using rPlace.ViewModels;
using Websocket.Client;

namespace rPlace.Views;
public partial class MainWindow : Window
{
    private bool mouseDown;
    private Point mouseLast;
    private Point mouseTravel;
    private Point lookingAtPixel;
    private WebsocketClient socket;
    private readonly HttpClient client = new();
    private readonly Cursor arrow = new (StandardCursorType.Arrow);
    private readonly Cursor cross = new(StandardCursorType.Cross);
    
    private PaletteViewModel PVM => (PaletteViewModel?) PaletteListBox.DataContext ?? new PaletteViewModel();
    
    private Point LookingAtPixel
    {
        get
        {
            lookingAtPixel = new Point(
                Math.Clamp(Math.Floor(MainGrid.ColumnDefinitions[0].ActualWidth / 2 - Board.Left), 0, Board.CanvasWidth ?? 500),
                Math.Clamp(Math.Floor(Height / 2 - Board.Top), 0, Board.CanvasHeight ?? 500)
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
        (int) Math.Clamp(Math.Floor(e.GetPosition(CanvasBackground).X - Board.Left), 0, Board.CanvasWidth ?? 500),
        (int) Math.Clamp(Math.Floor(e.GetPosition(CanvasBackground).Y - Board.Top), 0, Board.CanvasHeight ?? 500)
    );

    public MainWindow()
    {
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
                ToolsPanel.Classes = Classes.Parse("ToolsPanelClose");
            }
            else
            {
                if (ToolsPanel.Classes.Contains("ToolsPanelOpen")) return;
                ToolsPanel.Classes = Classes.Parse("ToolsPanelOpen");
            }
        });
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
        
        socket.ReconnectionHappened.Subscribe(info => Console.WriteLine("Reconnected to {0}, {1}", uri, info.Type));
        socket.MessageReceived.Subscribe(msg =>
        {
            var code = msg.Binary[0];
            switch (code)
            {
                case 6: //Incoming pixel someone else sent
                {
                    //if (BitConverter.IsLittleEndian) Array.Reverse(msg.Binary);
                    var i = 0;
                    foreach (var t in msg.Binary) Console.Write(t); Console.Write("\n");
                    while (i < msg.Binary.Length - 2)
                    {
                        // //seti(data.getUint32(i += 1), data.getUint8(i += 4))
                        var pos = BitConverter.ToInt32(msg.Binary, i += 1);
                        var col = msg.Binary[i += 4];
                        Console.WriteLine("c:{3} Incoming pixel {0}, {1}, {2}", pos % Board.CanvasWidth ?? 500, pos / Board.CanvasWidth ?? 500, col, code);
                    }
                    break;
                }
                case 7: //Sending back what you placed
                {
                    var pos = BitConverter.ToUInt32(msg.Binary.ToArray(), 5);
                    var col = msg.Binary[9];
                    Console.WriteLine("c:{3} Pixel coming back {0}, {1}, {2}", pos % Board.CanvasWidth ?? 500, pos / Board.CanvasWidth ?? 500, col, code);
                    break;
                }
            }
        });
        socket.DisconnectionHappened.Subscribe(info => Console.WriteLine("Disconnected from {0}, {1}", uri, info.Exception));
        
        await socket.Start();
    }

    private void OnRollbackButtonPressed()
    {
        //var buffer = new Uint8Array( w * h + 7 ), i = xxxx + yyyy * WIDTH
        //Object.assign(buffer, [99, w, h, i >> 24, i >> 16, i >> 8, i])
        //foreach (var selection in Board.Selections)
        //{
        //    var stream = new MemoryStream((int) (selection.Tl.X - selection.Br.X) * (int) (selection.Tl.Y - selection.Br.Y) + 7);
        //}
    }

    private async Task FetchCacheBackuplist()
    {
        var responseBody = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backuplist"))).Content.ReadAsStringAsync();
        var stack = new Stack(responseBody.Split("\n"));
        stack.Pop();
        stack.Push("place");
        var backupArr = (object[]) stack.ToArray();
        CanvasDropdown.Items = backupArr;
    }
    
    private async Task<HttpResponseMessage> Fetch(string? uri)
    {
        try
        {
            var response = await client.GetAsync(uri);
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch (HttpRequestException e) { Console.WriteLine(e); }
        return new HttpResponseMessage();
    }

    private async Task<Bitmap?> CreateCanvasPreviewImage<T>(T input)
    {
        var placeFile = input switch
        {
            string uri => await (await Fetch(uri)).Content.ReadAsByteArrayAsync(),
            byte[] board => board,
            _ => null
        };
        if (placeFile is null) return null;
        
        using var bmp = new SKBitmap(Board.CanvasWidth ?? 500, Board.CanvasHeight ?? 500, true);
        for (var i = 0; i < placeFile.Length; i++)
            bmp.SetPixel(i % Board.CanvasWidth ?? 500, i / Board.CanvasWidth ?? 500, PaletteViewModel.Colours.ElementAtOrDefault(placeFile[i])); //ElementAtOrDefault is safer than direct index
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
        PlaceConfigPanel.IsVisible = false;
        DownloadBtn.IsEnabled = true;
        await CreateConnection(ConfigWsInput.Text + ConfigAdminKeyInput.Text);
        await FetchCacheBackuplist();
        CanvasDropdown.SelectedIndex = 0;
        //Set the most recent place file to be the board background
        Board.Board = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "place"))).Content.ReadAsByteArrayAsync();
        
    }

    private void OnBackgroundMouseDown(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is null || !e.Source.Equals(CanvasBackground)) return; //stop bubbling
        mouseTravel = new Point(0, 0);
        mouseDown = true;
        if (SelectTool.IsChecked is true) Board.StartSelection(MouseOverPixel(e), MouseOverPixel(e));
    }

    private void OnBackgroundMouseMove(object? sender, PointerEventArgs e)
    {
        if (e.Source is null || !e.Source.Equals(CanvasBackground)) return;
        if (mouseDown)
        {
            //If left mouse button, go to colour picker mode from the canvas instead
            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                if (e.GetPosition(CanvasBackground).X - Board.Left < 0 || e.GetPosition(CanvasBackground).X - Board.Left > (Board.CanvasWidth ?? 500) * Board.Zoom ||
                    e.GetPosition(CanvasBackground).Y - Board.Left < 0 || e.GetPosition(CanvasBackground).Y - Board.Left > (Board.CanvasHeight ?? 500) * Board.Zoom) return;
                CursorIndicatorRectangle.IsVisible = true;
                Canvas.SetLeft(CursorIndicatorRectangle, e.GetPosition(CanvasBackground).X + 8);
                Canvas.SetTop(CursorIndicatorRectangle, e.GetPosition(CanvasBackground).Y + 8);
                Cursor = cross;
                //If mouse if over board, then get pixel colour at that position.
                var pxCol = Board.ColourAt((int)Math.Floor(e.GetPosition(CanvasBackground).X), (int)Math.Floor(e.GetPosition(CanvasBackground).Y));
                if (pxCol is null) return;
                if (PaletteViewModel.Colours.IndexOf((SKColor) pxCol) > 0) PVM.CurrentColour = PaletteViewModel.Colours.IndexOf((SKColor) pxCol);
                CursorIndicatorRectangle.Background = new SolidColorBrush(new Color(pxCol.Value.Alpha, pxCol.Value.Red, pxCol.Value.Green, pxCol.Value.Blue));
                return;
            }
            if (SelectTool.IsChecked is true)
            {
                Board.UpdateSelection(null, MouseOverPixel(e));
                return;
            }
            if (PVM.CurrentColour is not null) //drag place pixels
            {
                var px = new Pixel {
                    Colour = PVM.CurrentColour ?? 0,
                    X = (int) Math.Floor(MouseOverPixel(e).X),
                    Y = (int) Math.Floor(MouseOverPixel(e).Y),
                    Width = Board.CanvasWidth ?? 500,
                    Height = Board.CanvasHeight ?? 500
                };
                //Board.Set(px);
                //SetPixels(px, 20);
                return;
            }
            
            //Multiply be 1/zoom so that it always moves at a speed to make it seem to drag with the mouse regardless of zoom level
            Board.Left += (float) (e.GetPosition(CanvasBackground).X - mouseLast.X) * (1 / Board.Zoom);
            Board.Top += (float) (e.GetPosition(CanvasBackground).Y - mouseLast.Y) * (1 / Board.Zoom);
            //Clamp values
            Board.Left = (float) Math.Clamp(Board.Left, MainGrid.ColumnDefinitions[0].ActualWidth / 2 - (Board.CanvasWidth ?? 500) * Board.Zoom, MainGrid.ColumnDefinitions[0].ActualWidth / 2);
            Board.Top = (float) Math.Clamp(Board.Top, Height / 2 - (Board.CanvasHeight ?? 500) * Board.Zoom, Height / 2);
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
        if (e.Source is null || !e.Source.Equals(CanvasBackground)) return;
        mouseDown = false;
        
        //click place pixel
        if (PVM.CurrentColour is null) return;
        var px = new Pixel {
            Colour = PVM.CurrentColour ?? 0,
            X = (int) Math.Floor(MouseOverPixel(e).X),
            Y = (int) Math.Floor(MouseOverPixel(e).Y),
            Width = Board.CanvasWidth ?? 500,
            Height = Board.CanvasHeight ?? 500
        };
        //SetPixels(px, 50);
        //Board.Set(px);
        for (var i = 0; i < px.ToByteArray().Length; i++) Console.Write(px.ToByteArray()[i]);
        PVM.CurrentColour = null;
    }

    //https://github.com/rslashplace2/rslashplace2.github.io/blob/1cc30a12f35a6b0938e538100d3337228087d40d/index.html#L531
    private void OnBackgroundWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        //Board.Left -= (float) (e.GetPosition(this).X - MainGrid.ColumnDefinitions[0].ActualWidth / 2) / 50; //Board.Top -= (float) (e.GetPosition(this).Y - Height / 2) / 50;
        LookingAtPixel = new Point((float) (e.GetPosition(Board).X), (float) (e.GetPosition(Board).Y));
        Board.Zoom += (float) e.Delta.Y / 10;
    }

    private async void OnCanvasDropdownSelectionChanged(object? _, SelectionChangedEventArgs e)
    {
        if (CanvasDropdown is null) return;
        var backupName = CanvasDropdown.SelectedItem as string ?? "place";
        PreviewImg.Source = await CreateCanvasPreviewImage(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", backupName));
        
        //If we are viewing selected date only through the selection, then we are still technically on the pseudo-live canvas, viewselected only applies when we are on the live canvas
        if (ViewSelectedDate.IsChecked is false)
        {
            Board.Board = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", backupName))).Content.ReadAsByteArrayAsync();
            //If we are not looking at most recent backup, show a warning that we will not be able to modify it at all & disable tools.
            ToolsInformation.IsVisible = CanvasDropdown.SelectedIndex != 0;
            LiveCanvasWarning.IsVisible = CanvasDropdown.SelectedIndex != 0;
            PaintbrushTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
            RubberTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
            SelectTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
        }
        else
        {
            //TODO: This is essentially same as OnViewSelectedDateClicked
            Board.Board = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "place"))).Content.ReadAsByteArrayAsync();
            Board.SelectionBoard = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", backupName))).Content.ReadAsByteArrayAsync();
            ToolsInformation.IsVisible = false;
            LiveCanvasWarning.IsVisible = false;
            PaintbrushTool.IsEnabled = true;
            RubberTool.IsEnabled = true;
            SelectTool.IsEnabled = true;
        }
    }
    
    private async void OnViewSelectedDateChecked(object? sender, RoutedEventArgs e)
    {
            Board.Board = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "place"))).Content.ReadAsByteArrayAsync();
            var backupName = CanvasDropdown.SelectedItem as string ?? "place";
            Board.SelectionBoard = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", backupName))).Content.ReadAsByteArrayAsync();
            ToolsInformation.IsVisible = false;
            LiveCanvasWarning.IsVisible = false;
            PaintbrushTool.IsEnabled = true;
            RubberTool.IsEnabled = true;
            SelectTool.IsEnabled = true;
    }

    private async void OnViewSelectedDateUnchecked(object? sender, RoutedEventArgs e)
    {
        Board.Board = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", (string) CanvasDropdown.SelectedItem!))).Content.ReadAsByteArrayAsync();
        //If we are not looking at most recent backup, show a warning that we will not be able to modify it at all & disable tools.
        ToolsInformation.IsVisible = CanvasDropdown.SelectedIndex != 0;
        LiveCanvasWarning.IsVisible = CanvasDropdown.SelectedIndex != 0;
        PaintbrushTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
        RubberTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
        SelectTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
    }
    
    private void OnResetCanvasViewPressed(object? _, RoutedEventArgs e)
    {
        Board.Left = 0;
        Board.Top = 0;
        Board.Zoom = 1;
    }
    
    private void OnSelectColourClicked(object? sender, RoutedEventArgs e) => Palette.IsVisible = true;
    private void OnPaletteDoneButtonClicked(object? sender, RoutedEventArgs e) => Palette.IsVisible = false;
    private void OnPaletteSelectionChanged(object? sender, SelectionChangedEventArgs e) =>  PVM.CurrentColour = (sender as ListBox)?.SelectedIndex ?? PVM.CurrentColour;
    private async void OnDownloadPreviewPressed(object? sender, RoutedEventArgs e)
    {
        var path = await ShowSaveFileDialog(
            (CanvasDropdown.SelectedItem as string ?? "place") + "_preview.png",
            "Download place file image to system"
        );
        if (path is null) return;
        var placeImg = await CreateCanvasPreviewImage(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", CanvasDropdown.SelectedItem as string ?? "place"));
        placeImg?.Save(path);
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
        if (radius is 0 or 1)
        {
            Board.Set(px);
            //socket.Send(px.ToByteArray());
            return;
        }
        
        for (var x = 0 - radius / 2; x < radius / 2; x++)
        {
            for (var y = 0 - radius / 2; y < radius / 2; y++)
            {
                var radiusPx = px.Clone();
                radiusPx.X += x;
                radiusPx.Y += y;
                Board.Set(radiusPx);
                //socket.Send(px.ToByteArray());
            }
        }
    }

    private void OnToggleThemePressed(object? sender, RoutedEventArgs e)
    {
        var currentStyle = (FluentTheme) Application.Current?.Styles[4]!;
        currentStyle.Mode = currentStyle.Mode == FluentThemeMode.Dark ? FluentThemeMode.Light : FluentThemeMode.Dark;
    }
}
