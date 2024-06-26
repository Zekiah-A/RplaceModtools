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
using Avalonia.Platform.Storage;
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
    private const int HandleClickRadius = 8;
    private const int MinSelectionSize = 4;

    private Point LookingAtPixel
    {
        get
        {
            lookingAtPixel = new Point(
                Math.Clamp(Math.Floor(MainGrid.ColumnDefinitions[0].ActualWidth / 2 - (Board.Left * Board.Zoom)), 0, viewModel.CanvasWidth),
                Math.Clamp(Math.Floor(Height / 2 - (Board.Top * Board.Zoom)), 0, viewModel.CanvasHeight)
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

    /// <summary>
    /// Gives the mouse position in board cordinates (relative to board).
    /// Result will always be clamped between 0 and board dimension in the corresponding direction.
    /// </summary>
    private Point MouseOverPixel(PointerEventArgs e)
    {
        // Zooming the board actually affects it's left and top as it zooms from the __WINDOW__ top left.
        // we have to account for both the board drifting and the cordinate shift as a result of natural board
        // zooming.
        var boardLeft = Board.Left * Board.Zoom;
        var boardTop = Board.Top * Board.Zoom;
        var fromBoardX = e.GetPosition(CanvasBackground).X - boardLeft;
        var fromBoardY = e.GetPosition(CanvasBackground).Y - boardTop;

        var relativePos = new Point(
            (int) Math.Clamp(Math.Floor(fromBoardX / Board.Zoom), 0, viewModel.CanvasWidth),
            (int) Math.Clamp(Math.Floor(fromBoardY / Board.Zoom), 0, viewModel.CanvasHeight));

        return relativePos;
    }

    public MainWindow()
    {
        viewModel = App.Current.Services.GetRequiredService<MainWindowViewModel>();
        DataContext = viewModel;
        
        InitializeComponent();

        viewModel.BoardSetPixel = Board.Set;
        viewModel.BoardUnsetPixel = Board.Unset;
        viewModel.StartSelection = Board.StartSelection;
        viewModel.UpdateSelection = Board.UpdateSelection;
        viewModel.RemoveSelection = Board.RemoveSelection;
        viewModel.ClearSelections = Board.ClearSelections;

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
                MainGrid.Classes.Remove("Portrait");
            }
            else
            {
                if (!MainGrid.Classes.Contains("Portrait"))
                {
                    MainGrid.Classes.Add("Portrait");
                }
            }
        });
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
                && viewModel.CurrentSelection.TopLeft.X - HandleClickRadius < mousePosition.X && Board.CurrentSelection.TopLeft.Y - HandleClickRadius < mousePosition.Y
                && viewModel.CurrentSelection.BottomRight.X + HandleClickRadius > mousePosition.X && Board.CurrentSelection.BottomRight.Y + HandleClickRadius > mousePosition.Y)
            {
                if (Math.Abs(viewModel.CurrentSelection.TopLeft.X - mousePosition.X) < HandleClickRadius * Board.Zoom)
                {
                    if (Math.Abs(viewModel.CurrentSelection.TopLeft.Y - mousePosition.Y) < HandleClickRadius * Board.Zoom) // Tl handle
                    {
                        viewModel.CurrentHandle = SelectionHandle.TopLeft;
                        Cursor = new Cursor(StandardCursorType.TopLeftCorner);
                    }
                    else if (Math.Abs(viewModel.CurrentSelection.BottomRight.Y - mousePosition.Y) < HandleClickRadius * Board.Zoom) // Bl handle
                    {
                        viewModel.CurrentHandle = SelectionHandle.BottomLeft;
                        Cursor = new Cursor(StandardCursorType.BottomLeftCorner);
                    }
                }
                else if (Math.Abs(viewModel.CurrentSelection.BottomRight.X - mousePosition.X) < HandleClickRadius * Board.Zoom)
                {
                    if (Math.Abs(viewModel.CurrentSelection.BottomRight.Y - mousePosition.Y) < HandleClickRadius * Board.Zoom) // Br handle
                    {
                        viewModel.CurrentHandle = SelectionHandle.BottomRight;
                        Cursor = new Cursor(StandardCursorType.BottomRightCorner);
                    }
                    else if (Math.Abs(viewModel.CurrentSelection.TopLeft.Y - mousePosition.Y) < HandleClickRadius * Board.Zoom) // Tr handle
                    {
                        viewModel.CurrentHandle = SelectionHandle.TopRight;
                        Cursor = new Cursor(StandardCursorType.TopRightCorner);
                    }
                }
                else
                {
                    viewModel.CurrentHandle = SelectionHandle.None;
                }
            }
            else
            {
                var newSelection = Board.StartSelection(mousePosition, new Point(mousePosition.X + HandleClickRadius * Board.Zoom, mousePosition.Y + HandleClickRadius * Board.Zoom));
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
                
                var pixelColour = paletteVm.PaletteColours[colourIndex];
                paletteVm.CurrentColour = (byte) colourIndex;
                CursorIndicatorRectangle.Background = new SolidColorBrush(
                    new Color(pixelColour.Alpha, pixelColour.Red, pixelColour.Green, pixelColour.Blue));
                return;
            }
            if (viewModel is { CurrentTool: Tool.Select, CurrentSelection: not null })
            {
                var mousePosition = MouseOverPixel(e);
                if (viewModel.CurrentHandle == SelectionHandle.None)
                {
                    return;
                }

                var topLeft = viewModel.CurrentHandle switch
                {
                    SelectionHandle.TopLeft => new Point(Math.Min(mousePosition.X, viewModel.CurrentSelection.BottomRight.X - MinSelectionSize * Board.Zoom),
                            Math.Min(mousePosition.Y, viewModel.CurrentSelection.BottomRight.Y -MinSelectionSize * Board.Zoom)),
                    SelectionHandle.BottomLeft => new Point(Math.Min(mousePosition.X, viewModel.CurrentSelection.BottomRight.X - MinSelectionSize * Board.Zoom),
                        viewModel.CurrentSelection.TopLeft.Y),
                    SelectionHandle.TopRight => new Point(viewModel.CurrentSelection.TopLeft.X,
                            Math.Min(mousePosition.Y, viewModel.CurrentSelection.BottomRight.Y -MinSelectionSize * Board.Zoom)),
                    _ => viewModel.CurrentSelection.TopLeft
                };
                var bottomRight = viewModel.CurrentHandle switch
                {
                    SelectionHandle.BottomLeft => new Point(viewModel.CurrentSelection.BottomRight.X,
                            Math.Max(mousePosition.Y, viewModel.CurrentSelection.TopLeft.Y + MinSelectionSize * Board.Zoom)),
                    SelectionHandle.BottomRight => new Point(Math.Max(mousePosition.X, viewModel.CurrentSelection.TopLeft.X + MinSelectionSize * Board.Zoom),
                            Math.Max(mousePosition.Y, viewModel.CurrentSelection.TopLeft.Y + MinSelectionSize * Board.Zoom)),
                    SelectionHandle.TopRight => new Point(Math.Max(mousePosition.X, viewModel.CurrentSelection.TopLeft.X + MinSelectionSize * Board.Zoom), 
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
            
            // Multiply be 1/zoom so that it always moves at a speed to make it seem to drag with the mouse regardless of zoom level
            Board.Left += (float) (e.GetPosition(CanvasBackground).X - mouseLast.X) * (1 / Board.Zoom);
            Board.Top += (float) (e.GetPosition(CanvasBackground).Y - mouseLast.Y) * (1 / Board.Zoom);
            
            //Clamp values
            Board.Left = (float)Math.Clamp(Board.Left,
                MainGrid.ColumnDefinitions[0].ActualWidth / 2 - viewModel.CanvasWidth * Board.Zoom,
                MainGrid.ColumnDefinitions[0].ActualWidth / 2);
            Board.Top = (float) Math.Clamp(Board.Top,
                Height / 2 - viewModel.CanvasHeight * Board.Zoom,
                Height / 2);
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
            Cursor = arrow;
        }
        
        // Click place pixel
        if (paletteVm.CurrentColour is null) return;
        var mousePos = MouseOverPixel(e);
        var pixel = new Pixel((int)Math.Floor(mousePos.X), (int)Math.Floor(mousePos.Y), viewModel.CanvasWidth, paletteVm.CurrentColour ?? 0);
        SetPixels(pixel, viewModel.CurrentPaintBrushRadius);
        
        paletteVm.CurrentColour = null;
    }

    //https://github.com/rslashplace2/rslashplace2.github.io/blob/1cc30a12f35a6b0938e538100d3337228087d40d/index.html#L531
    private void OnBackgroundWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        Board.Left -= (float) (e.GetPosition(this).X - MainGrid.ColumnDefinitions[0].ActualWidth / 2) / 50;
        Board.Top -= (float) (e.GetPosition(this).Y - Height / 2) / 50;
        //LookingAtPixel = new Point((float) e.GetPosition(Board).X * Board.Zoom, (float) e.GetPosition(Board).Y * Board.Zoom);
        //var pos = LookingAtPixel;
        Board.Zoom += (float) e.Delta.Y / 10;
        //LookingAtPixel = pos;
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
                Task.Delay((int) viewModel.Cooldown);
                if (viewModel.Socket is { IsRunning: true })
                {
                    viewModel.Socket.Send(brushStack.Pop().ToByteArray());
                }
            }
        });
    }

    private void OnAboutClicked(object? sender, RoutedEventArgs args)
    {
        AboutPanel.IsVisible = true;
    }

    private void OnCloseAboutClicked(object? sender, RoutedEventArgs args)
    {
        AboutPanel.IsVisible = false;
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
            liveChatVm.CurrentChannel.Messages.Add(new LiveChatMessage
            {
                Name = "!!",
                Message = "Commands:\n :help, displays commands,\n`:name username` sets your livechat username"
            });
        }
        else if (input.StartsWith(":name"))
        {
            viewModel.CurrentPreset.ChatUsername = ChatInput!.Text![5..].Trim();
            
            liveChatVm.CurrentChannel.Messages.Add(new LiveChatMessage
            {
                Name = "!!",
                Message = "Chat username set to " + viewModel.CurrentPreset.ChatUsername
            });
        }
        else if (viewModel.CurrentPreset.ChatUsername is null)
        {
            liveChatVm.CurrentChannel.Messages.Add(new LiveChatMessage
            {
                Name = "!!",
                Message = "No chat username set! Use command `:name username`, and use :help for more commands"
            });
        }
        else
        {
            var encodedMsg = Encoding.UTF8.GetBytes(input);
            var encodedChannel = Encoding.UTF8.GetBytes(liveChatVm.CurrentChannel.ChannelCode);
            var messageData = new Span<byte>(new byte[1 + 1 + 2 + encodedMsg.Length + 1 +
                encodedChannel.Length + (false ? 4 : 0)]); // TODO: Current reply
            
            var offset = 0;
            messageData[offset++] = 15;
            messageData[offset++] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(messageData[offset..(offset += 2)], (ushort)encodedMsg.Length);
            encodedMsg.CopyTo(messageData[offset..]);
            offset += encodedMsg.Length;
            messageData[offset++] = (byte) encodedChannel.Length;
            encodedChannel.CopyTo(messageData[offset..]);
            /*offset += (byte) encodedChannel.Length;
            if (currentReply)
            {
                BinaryPrimitives.WriteUInt32BigEndian(messageData[offset..], 0);
            }*/

            if (viewModel.Socket is { IsRunning: true })
            {
                viewModel.Socket.Send(messageData.ToArray());
            }
        }
    }
    
    private void OnModerateUserPressed(object? sender, PointerPressedEventArgs e)
    {
        
        ActionConfigPanel.IsVisible = true;
        viewModel.CurrentModerationAction = ModerationAction.None;
        viewModel.CurrentModerationMessageId = 0;
    }

    private void OnKickChatterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: LiveChatMessage message })
        {
            return;
        }
        
        ActionConfigPanel.IsVisible = true;
        viewModel.CurrentModerationAction = ModerationAction.Kick;
        viewModel.CurrentModerationMessageId = message.MessageId;
    }
    
    private void OnMuteChatterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: LiveChatMessage message })
        {
            return;
        }
        
        ActionConfigPanel.IsVisible = true;
        viewModel.CurrentModerationAction = ModerationAction.Mute;
        viewModel.CurrentModerationMessageId = message.MessageId;
    }

    private void OnBanChatterPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: LiveChatMessage message })
        {
            return;
        }
        
        ActionConfigPanel.IsVisible = true;
        viewModel.CurrentModerationAction = ModerationAction.Ban;
        viewModel.CurrentModerationMessageId = message.MessageId;
    }

    private void OnActionConfigSubmitPressed(object? sender, RoutedEventArgs e)
    {
        switch (viewModel.CurrentModerationAction)
        {
            case ModerationAction.Kick:
            {
                var reasonBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationReason);
                var uidBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationMessageId.ToString());
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
                var uidBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationMessageId.ToString());
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
                var uidBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationMessageId.ToString());
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
                    uidBytes = Encoding.UTF8.GetBytes(viewModel.CurrentModerationMessageId.ToString());
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
        viewModel.CurrentModerationMessageId = 0;
    }
    
    private void OnLoadLocalClicked(object? sender, RoutedEventArgs e)
    {
        Console.WriteLine($"Error: {nameof(OnLoadLocalClicked)} not implemented!");
        return;
    }

    private void OnLoadImageClicked(object? sender, RoutedEventArgs e)
    {
        ImageConfigPanel.IsVisible = true;
    }

    private async void OnLoadImageFromFileClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel is null)
        {
            Console.WriteLine("Failed to load image from file, topLevel was null.");
            return;
        }
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open image file",
            AllowMultiple = false
        });

        if (files.Count >= 1)
        {
            await using var stream = await files[0].OpenReadAsync();
            // Hand over image to VM
        }
    }
    
    private async void OnLoadImageFromUrlClicked(object? sender, RoutedEventArgs e)
    {
        var imageUrl = ImageUrl.Text;
        if (imageUrl is null)
        {
            Console.WriteLine("Could not load image from url, url was null.");
            return;
        }

        using var httpClient = new HttpClient();
        try
        {
            var response = await httpClient.GetAsync(imageUrl);
            if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentType != null && response.Content.Headers.ContentType.MediaType.StartsWith("image"))
                {
                    // Download the image content
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();

                    // Save the image to a file or process the image data as needed
                    // For example, save to a file:
                    File.WriteAllBytes("downloaded_image.jpg", imageBytes);

                    Console.WriteLine("Image downloaded successfully!");
                }
                else
                {
                    Console.WriteLine("The URL does not point to an image.");
                }
            }
            else
            {
                Console.WriteLine($"Failed to download image. Status code: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
    }
}
