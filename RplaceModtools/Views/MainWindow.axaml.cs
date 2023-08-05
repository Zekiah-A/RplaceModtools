using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DynamicData;
using LibGit2Sharp;
using Microsoft.Extensions.DependencyInjection;
using RplaceModtools.Models;
using SkiaSharp;
using RplaceModtools.ViewModels;
using Timer = System.Timers.Timer;
using Avalonia.Themes.Fluent;

namespace RplaceModtools.Views;
public partial class MainWindow : Window
{
    private bool mouseDown;
    private Point mouseLast;
    private Point mouseTravel;
    private Point lookingAtPixel;
    private readonly Cursor arrow = new (StandardCursorType.Arrow);
    private readonly Cursor cross = new(StandardCursorType.Cross);
    private MainWindowViewModel viewModel;
    private PaletteViewModel paletteVm = App.Current.Services.GetRequiredService<PaletteViewModel>();
    private LiveChatViewModel liveChatVm = App.Current.Services.GetRequiredService<LiveChatViewModel>();

    private Point LookingAtPixel
    {
        get
        {
            lookingAtPixel = new Point(
                Math.Clamp(Math.Floor(MainGrid.ColumnDefinitions[0].ActualWidth / 2 - Board.Left), 0, viewModel.CanvasWidth),
                Math.Clamp(Math.Floor(Height / 2 - Board.Top), 0, viewModel.CanvasHeight)
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
        (int) Math.Clamp(Math.Floor((e.GetPosition(CanvasBackground).X - Board.Left) * Board.Zoom), 0, viewModel.CanvasWidth),
        (int) Math.Clamp(Math.Floor((e.GetPosition(CanvasBackground).Y - Board.Top) * Board.Zoom), 0, viewModel.CanvasHeight)
    );

    public MainWindow()
    {
        // TODO: View should not actually have access to VM
        viewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = viewModel;
        
        InitializeComponent();

        viewModel.BoardSetPixel = pixel => Board.Set(pixel);
        viewModel.StartSelection = (tl, br) => Board.StartSelection(tl, br);
        viewModel.UpdateSelection = (selection, tl, br) => Board.UpdateSelection(selection, tl, br);
        viewModel.RemoveSelection = selection => Board.RemoveSelection(selection);
        viewModel.ClearSelections = () => Board.ClearSelections();

        PaletteListBox.DataContext = App.Current.Services.GetRequiredService<PaletteViewModel>();
        LiveChatGridContainer.DataContext = App.Current.Services.GetRequiredService<LiveChatViewModel>();
        
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
    
    
    private async Task<Bitmap?> CreateCanvasPreviewImage<T>(T input)
    {
        async Task<byte[]?> FetchBoardAsync(Uri uri)
        {
            var boardResponse = await viewModel.Fetch(uri);
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
        
        // TODO: viewModel.CanvasWidth, (int)viewModel.CanvasHeight could be wrong, we need to ensure board is properly unpacked
        var imageInfo = new SKImageInfo((int) viewModel.CanvasWidth, (int) viewModel.CanvasHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;

        Parallel.For(0, placeFile.Length, i =>
        {
            var x = (int) (i % viewModel.CanvasWidth);
            var y = (int) (i / viewModel.CanvasWidth);
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
    
    private void OnBackgroundMouseDown(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is null || !e.Source.Equals(CanvasBackground))
        {
            return; //stop bubbling
        }

        mouseTravel = new Point(0, 0);
        mouseDown = true;

        if (viewModel.CurrentTool != Tool.Select)
        {
            return;
        }
        
        var mousePosition = MouseOverPixel(e);
        foreach (var selection in viewModel.Selections)
        {
            if (selection.TopLeft.X < mousePosition.X && selection.TopLeft.Y < mousePosition.Y &&
                selection.BottomRight.X > mousePosition.X && selection.BottomRight.Y > mousePosition.Y)
            {
                if (e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
                {
                    Board.RemoveSelection(selection);
                }
                else
                {
                    viewModel.CurrentSelection = selection;
                }
                    
                return;
            }
        }

        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
        {
            // Try find if we are near any of the current selection's handles,
            // if so we current that handle, otherwise, we will create a new selection
            if (viewModel.CurrentSelection is not null
                && viewModel.CurrentSelection.TopLeft.X - 8 < mousePosition.X && Board.CurrentSelection.TopLeft.Y - 8 < mousePosition.Y
                && viewModel.CurrentSelection.BottomRight.X + 8 > mousePosition.X && Board.CurrentSelection.BottomRight.Y + 8 > mousePosition.Y)
            {
                if (Math.Abs(viewModel.CurrentSelection.TopLeft.X - mousePosition.X) < 4)
                {
                    if (Math.Abs(viewModel.CurrentSelection.TopLeft.Y - mousePosition.Y) < 4) // Tl handle
                    {
                        viewModel.CurrentHandle = SelectionHandle.TopLeft;
                    }
                    else if (Math.Abs(viewModel.CurrentSelection.BottomRight.Y - mousePosition.Y) < 4) // Bl handle
                    {
                        viewModel.CurrentHandle = SelectionHandle.BottomLeft;
                    }
                }
                else if (Math.Abs(viewModel.CurrentSelection.BottomRight.X - mousePosition.X) < 4)
                {
                    if (Math.Abs(viewModel.CurrentSelection.BottomRight.Y - mousePosition.Y) < 4) // Br handle
                    {
                        viewModel.CurrentHandle = SelectionHandle.BottomRight;
                    }
                    else if (Math.Abs(viewModel.CurrentSelection.TopLeft.Y - mousePosition.Y) < 4) // Tr handle
                    {
                        viewModel.CurrentHandle = SelectionHandle.TopRight;
                    }
                }
                else
                {
                    viewModel.CurrentHandle = SelectionHandle.None;
                }
            }
            else
            {
                var newSelection = Board.StartSelection(mousePosition, new Point(mousePosition.X + 4, mousePosition.Y + 4));
                viewModel.CurrentSelection = newSelection;
                viewModel.CurrentHandle = SelectionHandle.BottomRight;
            }
        }
    }

    private int BoardColourAt(uint x, uint y)
    {
        var index = (int)(x + y * viewModel.CanvasWidth);
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

        return viewModel.Board?.ElementAt(index) ?? -1;
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
                    e.GetPosition(CanvasBackground).X - Board.Left > viewModel.CanvasWidth * Board.Zoom ||
                    e.GetPosition(CanvasBackground).Y - Board.Left < 0 ||
                    e.GetPosition(CanvasBackground).Y - Board.Left > viewModel.CanvasHeight * Board.Zoom)
                {
                    return;
                }
                
                CursorIndicatorRectangle.IsVisible = true;
                Canvas.SetLeft(CursorIndicatorRectangle, e.GetPosition(CanvasBackground).X + 8);
                Canvas.SetTop(CursorIndicatorRectangle, e.GetPosition(CanvasBackground).Y + 8);
                Cursor = cross;
                
                //If mouse if over board, then get pixel colour at that position.
                var colourIndex = BoardColourAt((uint)Math.Floor(e.GetPosition(CanvasBackground).X),
                    (uint)Math.Floor(e.GetPosition(CanvasBackground).Y));
                if (colourIndex == -1)
                {
                    return;
                }
                
                var pixelColour = PaletteViewModel.Colours[colourIndex];
                paletteVm.CurrentColour = (byte) colourIndex;
                CursorIndicatorRectangle.Background = new SolidColorBrush(
                    new Color(pixelColour.Alpha, pixelColour.Red, pixelColour.Green, pixelColour.Blue));
                return;
            }
            if (viewModel.CurrentTool == Tool.Select && viewModel.CurrentSelection is not null)
            {
                var mousePosition = MouseOverPixel(e);
                if (viewModel.CurrentHandle == SelectionHandle.None)
                {
                    return;
                }

                var topLeft = viewModel.CurrentHandle switch
                {
                    SelectionHandle.TopLeft => new Point(Math.Min(mousePosition.X, viewModel.CurrentSelection.BottomRight.X - 4),
                            Math.Min(mousePosition.Y, viewModel.CurrentSelection.BottomRight.Y - 4)),
                    SelectionHandle.BottomLeft => new Point(Math.Min(mousePosition.X, viewModel.CurrentSelection.BottomRight.X - 4),
                        viewModel.CurrentSelection.TopLeft.Y),
                    SelectionHandle.TopRight => new Point(viewModel.CurrentSelection.TopLeft.X,
                            Math.Min(mousePosition.Y, viewModel.CurrentSelection.BottomRight.Y - 4)),
                    _ => viewModel.CurrentSelection.TopLeft
                };
                var bottomRight = Board.CurrentHandle switch
                {
                    SelectionHandle.BottomLeft => new Point(viewModel.CurrentSelection.BottomRight.X,
                            Math.Max(mousePosition.Y, viewModel.CurrentSelection.TopLeft.Y + 4)),
                    SelectionHandle.BottomRight => new Point(Math.Max(mousePosition.X, viewModel.CurrentSelection.TopLeft.X + 4),
                            Math.Max(mousePosition.Y, viewModel.CurrentSelection.TopLeft.Y + 4)),
                    SelectionHandle.TopRight => new Point(Math.Max(mousePosition.X, viewModel.CurrentSelection.TopLeft.X + 4), 
                            viewModel.CurrentSelection.BottomRight.Y),
                    _ => viewModel.CurrentSelection.BottomRight
                };

                Board.UpdateSelection(viewModel.CurrentSelection, topLeft, bottomRight);
                return;
            }
            if (paletteVm.CurrentColour is not null) //drag place pixels
            {
                var mousePos = MouseOverPixel(e);
                var pixel = new Pixel((int)Math.Floor(mousePos.X), (int)Math.Floor(mousePos.Y), viewModel.CanvasWidth, paletteVm.CurrentColour ?? 0);
                SetPixels(pixel, viewModel.CurrentPaintBrushRadius);
                return;
            }
            
            //Multiply be 1/zoom so that it always moves at a speed to make it seem to drag with the mouse regardless of zoom level
            Board.Left += (float) (e.GetPosition(CanvasBackground).X - mouseLast.X) * (1 / Board.Zoom);
            Board.Top += (float) (e.GetPosition(CanvasBackground).Y - mouseLast.Y) * (1 / Board.Zoom);
            
            //Clamp values
            Board.Left = (float)Math.Clamp(Board.Left,
                MainGrid.ColumnDefinitions[0].ActualWidth / 2 - viewModel.CanvasWidth * Board.Zoom,
                MainGrid.ColumnDefinitions[0].ActualWidth / 2);
            Board.Top = (float) Math.Clamp(Board.Top, Height / 2 - (viewModel.CanvasHeight) * Board.Zoom, Height / 2);
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

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            viewModel.CurrentHandle = SelectionHandle.None;
        }
        
        //click place pixel
        if (paletteVm.CurrentColour is null) return;
        var mousePos = MouseOverPixel(e);
        var pixel = new Pixel((int)Math.Floor(mousePos.X), (int)Math.Floor(mousePos.Y), viewModel.CanvasWidth, paletteVm.CurrentColour ?? 0);
        SetPixels(pixel, viewModel.CurrentPaintBrushRadius);
        
        paletteVm.CurrentColour = null;
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
        paletteVm.CurrentColour = (byte) ((sender as ListBox)?.SelectedIndex ?? paletteVm.CurrentColour ?? 0);
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
            if (viewModel.Socket is { IsRunning: true })
            {
                viewModel.Socket.Send(px.ToByteArray());
            }
            return;
        }

        var brushStack = new Stack<Pixel>();
        var realPos = px.GetPosition(viewModel.CanvasWidth);
        if (viewModel.CurrentBrushShape == BrushShape.Circular)
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
                    if (pixelX < 0 || pixelX > viewModel.CanvasWidth || pixelY < 0 || pixelY > viewModel.CanvasHeight)
                    {
                        continue;
                    }

                    var radiusPx = px.Clone();
                    radiusPx.SetPosition(pixelX, pixelY, viewModel.CanvasWidth);
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
                    radiusPx.SetPosition((uint)(realPos.X + x), (uint)(realPos.Y + y), viewModel.CanvasWidth);
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
                if (viewModel.Socket is { IsRunning: true })
                {
                    viewModel.Socket.Send(brushStack.Pop().ToByteArray());
                }
            }
        });
    }

    private void OnOpenGithubClicked(object? sender, RoutedEventArgs e)
    {
        string? processName = null;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            processName = "xdg-open";
        }
        else if (OperatingSystem.IsWindows())
        {
            processName = "";
        }
        else if (OperatingSystem.IsMacOS())
        {
            processName = "open";
        }
        if (processName is null)
        {
            return;
        }

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
        if (viewModel.CurrentPreset is null)
        {
            Console.WriteLine("Could not send chat message, current preset was null???");
            return;
        }
        
        if (input.StartsWith(":help"))
        {
            liveChatVm.CurrentChannel.Messages.Add(new ChatMessage
            {
                Name = "!!",
                Message = "Commands:\n :help, displays commands,\n`:name username` sets your livechat username"
            });
        }
        else if (input.StartsWith(":name"))
        {
            viewModel.CurrentPreset.ChatUsername = ChatInput.Text[5..].Trim();
            
            liveChatVm.CurrentChannel.Messages.Add(new ChatMessage
            {
                Name = "!!",
                Message = "Chat username set to " + viewModel.CurrentPreset.ChatUsername
            });
        }
        else if (viewModel.CurrentPreset.ChatUsername is null)
        {
            liveChatVm.CurrentChannel.Messages.Add(new ChatMessage
            {
                Name = "!!",
                Message = "No chat username set! Use command `:name username`, and use :help for more commands"
            });
        }
        else
        {
            var chatBuilder = new StringBuilder();
            chatBuilder.AppendLine(input);
            chatBuilder.AppendLine(viewModel.CurrentPreset.ChatUsername);
            chatBuilder.AppendLine(liveChatVm.CurrentChannel.ChannelName);
            chatBuilder.AppendLine("live");
            chatBuilder.AppendLine("0");
            chatBuilder.AppendLine("0");
            if (viewModel.Socket is { IsRunning: true })
            {
                viewModel.Socket.Send(Encoding.UTF8.GetBytes("\x0f" + chatBuilder));
            }
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
                if (viewModel.Socket is { IsRunning: true })
                {
                    viewModel.Socket.Send(kickBuffer);
                }
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
                if (viewModel.Socket is { IsRunning: true })
                {
                    viewModel.Socket.Send(muteBuffer);
                }
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
                if (viewModel.Socket is { IsRunning: true })
                {
                    viewModel.Socket.Send(muteBuffer);
                }
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
                if (viewModel.Socket is { IsRunning: true })
                {
                    viewModel.Socket.Send(captchaBuffer);
                }
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
}
