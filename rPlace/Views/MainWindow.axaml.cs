using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Input;
using System.Net.WebSockets;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DynamicData;
using SkiaSharp;
using rPlace.ViewModels;

namespace rPlace.Views;
public partial class MainWindow : Window
{
    public int? CurrentColour;
    private ClientWebSocket? ws;
    private bool mouseDown;
    private Vector2 mouseLast = Vector2.Zero;
    private Dictionary<string, Bitmap> cachedCanvasPreviews = new();
    private readonly HttpClient client = new();
    private static Vector2 lookingAtPixel;
    private readonly Cursor[] cursors = { new(StandardCursorType.Arrow), new(StandardCursorType.Cross) };
    private bool cacheCanvases;
    
    private Vector2 LookingAtPixel
    {
        get
        {   //Pixel we are looking at is 1/2screen width - board.left, to essentially get remainder
            lookingAtPixel = new Vector2((float) Math.Clamp(Math.Floor(MainGrid.ColumnDefinitions[0].ActualWidth / 2 - Board.Left), 0, 500), (float) Math.Clamp(Math.Floor(Height / 2 - Board.Top), 0, 500));
            return lookingAtPixel;
        }
        set
        {
            lookingAtPixel = value;
            Board.Left = (float) Math.Floor(MainGrid.ColumnDefinitions[0].ActualWidth / 2 - lookingAtPixel.X);
            Board.Top = (float) Math.Floor(Height / 2 - lookingAtPixel.Y);
        }
    }

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
    
    private async Task CreateConnection(Uri uri, string adminKey)
    {
        ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri(uri + adminKey), CancellationToken.None);
        while (ws.State == WebSocketState.Open)
        {
            ArraySegment<byte> bytesReceived = new ArraySegment<byte>(new byte[1024]);
            WebSocketReceiveResult result = await ws.ReceiveAsync(bytesReceived, CancellationToken.None);
            Console.WriteLine(Encoding.UTF8.GetString(bytesReceived.Array!, 0, result.Count));
        }
    }

    private async Task FetchCacheBackuplist()
    {
        var responseBody = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backuplist"))).Content.ReadAsStringAsync();
        var stack = new Stack(responseBody.Split("\n"));
        stack.Pop();
        stack.Push("place");
        var backupArr = (object[]) stack.ToArray();
        CanvasDropdown.Items = backupArr;

        //Over time, this func will cache canvas previews as bitmaps so that we can load them faster when selected in the dropdown
        if (!cacheCanvases) return;
        for (int i = 0; i < stack.Count; i++)
            cachedCanvasPreviews.Add((string) backupArr[i], await CreateCanvasPreviewImage(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", (string) backupArr[i])));
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

    private async Task<Bitmap> CreateCanvasPreviewImage(string? uri)
    {
        var placeFile = await (await Fetch(uri)).Content.ReadAsByteArrayAsync();
        using var bmp = new SKBitmap(500, 500, true);
        for (int i = 0; i < placeFile.Length; i++)
            bmp.SetPixel(i % 500, i / 500, PaletteViewModel.Colours[placeFile[i]]);
        using var bitmap = bmp.Encode(SKEncodedImageFormat.Jpeg, 100);
        await using var imgStream = new MemoryStream();
        imgStream.Position = 0;
        bitmap.SaveTo(imgStream);
        imgStream.Seek(0, SeekOrigin.Begin);
        bmp.Dispose(); bitmap.Dispose();
        return new Bitmap(imgStream);
    }

    //App started
    private async void OnStartButtonPressed(object? _, RoutedEventArgs e)
    {
        PlaceConfigPanel.IsVisible = false;
        await CreateConnection(new Uri(ConfigWsInput.Text), ConfigAdminKeyInput.Text);
        PreviewImg.Source = await CreateCanvasPreviewImage(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "place"));
        //Load the backup list into the dropdown, than start caching previews
        await FetchCacheBackuplist();
        CanvasDropdown.SelectedIndex = 0;
        
        //Set the most recent place file to be the board background
        Board.Board = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "place"))).Content.ReadAsByteArrayAsync();
    }
    
    private void OnBackgroundMouseDown(object? sender, PointerPressedEventArgs e)
    {
        mouseDown = true;
        if (SelectTool.IsChecked is true) Board.StartSelection(new Point(lookingAtPixel.X, lookingAtPixel.Y), new Point(lookingAtPixel.X, lookingAtPixel.Y));
        
    }

    private async void OnBackgroundMouseMove(object? sender, PointerEventArgs e)
    {
        if (mouseDown)
        {
            //If left mouse button, go to colour picker mode from the canvas instead
            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                if (e.GetPosition(CanvasBackground).X - Board.Left < 0 || e.GetPosition(CanvasBackground).X - Board.Left > 500 * Board.Zoom ||
                    e.GetPosition(CanvasBackground).Y - Board.Left < 0 || e.GetPosition(CanvasBackground).Y - Board.Left > 500 * Board.Zoom) return;
                CursorIndicatorRectangle.IsVisible = true;
                Canvas.SetLeft(CursorIndicatorRectangle, e.GetPosition(CanvasBackground).X + 8);
                Canvas.SetTop(CursorIndicatorRectangle, e.GetPosition(CanvasBackground).Y + 8);
                Cursor = cursors[1];
                //If mouse if over board, then get pixel colour at that position.
                var pxCol = Board.ColourAt((int)Math.Floor(e.GetPosition(CanvasBackground).X), (int)Math.Floor(e.GetPosition(CanvasBackground).Y));
                if (pxCol is null) return;
                //CurrentColour = PaletteViewModel.Colours.IndexOf((SKColor) pxCol);
                CursorIndicatorRectangle.Background = new SolidColorBrush(new Color(pxCol.Value.Alpha, pxCol.Value.Red, pxCol.Value.Green, pxCol.Value.Blue));
                return;
            }
            if (SelectTool.IsChecked is true)
            {
                Board.UpdateSelection(null, e.GetPosition(Board));
                return;
            }
            
            //Multiply be 1/zoom so that it always moves at a speed to make it seem to drag with the mouse regardless of zoom level
            Board.Left += (float) (e.GetPosition(CanvasBackground).X - mouseLast.X) * (1 / Board.Zoom);
            Board.Top += (float) (e.GetPosition(CanvasBackground).Y - mouseLast.Y) * (1 / Board.Zoom);
            //Clamp values
            Board.Left = (float) Math.Clamp(Board.Left, MainGrid.ColumnDefinitions[0].ActualWidth / 2 - 500 * Board.Zoom, MainGrid.ColumnDefinitions[0].ActualWidth / 2);
            Board.Top = (float) Math.Clamp(Board.Top, Height / 2 - 500 * Board.Zoom, Height / 2);
        }
        else
        {
            Cursor = cursors[0];
            CursorIndicatorRectangle.IsVisible = false;
        }
        mouseLast = new Vector2((float) e.GetPosition(CanvasBackground).X, (float) e.GetPosition(CanvasBackground).Y);
    }
    private void OnBackgroundMouseRelease(object? sender, PointerReleasedEventArgs e)
    {
        mouseDown = false;
        
        //If we are not dragging, place pixel.
        if (e.GetPosition(CanvasBackground).X - mouseLast.X < 10 || e.GetPosition(CanvasBackground).Y - mouseLast.Y < 10 || CurrentColour is null) return;
        //(int) Math.Clamp(Math.Floor(e.GetPosition(canvasBackground).X - Board.Left), 0, 500)
        //(int) Math.Clamp(Math.Floor(e.GetPosition(canvasBackground).Y - Board.Top), 0, 500)
    }
    
    //https://github.com/rslashplace2/rslashplace2.github.io/blob/1cc30a12f35a6b0938e538100d3337228087d40d/index.html#L531
    private void OnBackgroundWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        //Board.Left -= (float) (e.GetPosition(this).X - MainGrid.ColumnDefinitions[0].ActualWidth / 2) / 50;
        //Board.Top -= (float) (e.GetPosition(this).Y - Height / 2) / 50;
        LookingAtPixel = new Vector2((float) (e.GetPosition(Board).X), (float) (e.GetPosition(Board).Y));
        Board.Zoom += (float) e.Delta.Y / 10;
    }

    private async void OnCanvasDropdownSelectionChanged(object? _, SelectionChangedEventArgs e)
    {
        if (CanvasDropdown is null) return;
        var backupName = CanvasDropdown.SelectedItem as string ?? "place";
        PreviewImg.Source = cachedCanvasPreviews.ContainsKey(backupName) ? 
            cachedCanvasPreviews[backupName] : await CreateCanvasPreviewImage(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", backupName));
        
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
    
    private async void OnViewSelectedDateChecked(object? _, RoutedEventArgs e)
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

    private async void OnViewSelectedDateUnchecked(object? _, RoutedEventArgs e)
    {
        Board.Board = await (await Fetch(Path.Join(this.FindControl<AutoCompleteBox>("ConfigFsInput").Text, "backups", (string) CanvasDropdown.SelectedItem!))).Content.ReadAsByteArrayAsync();
        //If we are not looking at most recent backup, show a warning that we will not be able to modify it at all & disable tools.
        ToolsInformation.IsVisible = CanvasDropdown.SelectedIndex != 0;
        LiveCanvasWarning.IsVisible = CanvasDropdown.SelectedIndex != 0;
        PaintbrushTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
        RubberTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
        SelectTool.IsEnabled = CanvasDropdown.SelectedIndex == 0;
    }

    private async void OnCacheCanvasChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is null) return;
        cacheCanvases = (bool) ((ToggleSwitch) sender).IsChecked!;
        if (cacheCanvases) await FetchCacheBackuplist();
    }

    private void OnSelectColourClicked(object? _, RoutedEventArgs e) => Palette.IsVisible = true;
    private void OnPaletteDoneButtonClicked(object? _, RoutedEventArgs e) => Palette.IsVisible = false;
    private void OnPaletteSelectionChanged(object? sender, SelectionChangedEventArgs e) => CurrentColour = (sender as ListBox)?.SelectedIndex ?? CurrentColour;
    
    private void OnResetCanvasViewPressed(object? _, PointerPressedEventArgs e)
    {
        Board.Left = 0;
        Board.Top = 0;
        Board.Zoom = 1;
    }
}
