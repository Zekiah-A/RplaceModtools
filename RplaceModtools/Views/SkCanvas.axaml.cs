using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using RplaceModtools.Models;
using RplaceModtools.ViewModels;
using SkiaSharp;

namespace RplaceModtools.Views;

public partial class SkCanvas : UserControl
{
    private object selectionsLock = new();
    private List<Selection> selections = new();
    private SelectionHandle currentHandle = SelectionHandle.None;

    private uint canvasWidth = 500;
    private uint canvasHeight = 500;
    private byte[]? board = null;
    private byte[]? changes = null;
    private byte[]? selectionBoard = null;
    private SKImage? boardCache;
    private SKImage? changesCache;
    private SKImage? selectionCanvasCache;
    private byte[]? socketPixels;

    private Selection? currentSelection;
    private float canvZoom = 1;
    private SKPoint canvPosition = SKPoint.Empty;
    private List<SKPaint> paints = new();
    
    public static readonly DirectProperty<SkCanvas, byte[]?> BoardProperty =
        AvaloniaProperty.RegisterDirect<SkCanvas, byte[]?>(nameof(Board),
            instance => instance.Board,
            (instance, value) => instance.Board = value);

    public static readonly DirectProperty<SkCanvas, byte[]?> ChangesProperty =
        AvaloniaProperty.RegisterDirect<SkCanvas, byte[]?>(nameof(Changes),
            instance => instance.Changes,
            (instance, value) => instance.Changes = value);
    
    public static readonly DirectProperty<SkCanvas, uint> CanvasWidthProperty =
        AvaloniaProperty.RegisterDirect<SkCanvas, uint>(nameof(CanvasWidth),
            instance => instance.CanvasWidth,
            (instance, value) => instance.CanvasWidth = value);

    public static readonly DirectProperty<SkCanvas, uint> CanvasHeightProperty =
        AvaloniaProperty.RegisterDirect<SkCanvas, uint>(nameof(CanvasHeight),
            instance => instance.CanvasHeight,
            (instance, value) => instance.CanvasHeight = value);
    
    public static readonly DirectProperty<SkCanvas, byte[]?> SelectionBoardProperty =
        AvaloniaProperty.RegisterDirect<SkCanvas, byte[]?>(nameof(SelectionBoard),
            instance => instance.SelectionBoard,
            (instance, value) => instance.SelectionBoard = value);
    
    public static readonly DirectProperty<SkCanvas, Selection?> CurrentSelectionProperty =
        AvaloniaProperty.RegisterDirect<SkCanvas, Selection?>(nameof(SelectionBoard),
            instance => instance.currentSelection,
            (instance, value) => instance.currentSelection = value);

    public static readonly DirectProperty<SkCanvas, List<Selection>> SelectionsProperty =
        AvaloniaProperty.RegisterDirect<SkCanvas, List<Selection>>(nameof(SelectionBoard),
            instance => instance.selections,
            (instance, value) => instance.selections = value);

    public static readonly DirectProperty<SkCanvas, SelectionHandle> CurrentHandleProperty =
        AvaloniaProperty.RegisterDirect<SkCanvas, SelectionHandle>(nameof(SelectionBoard),
            instance => instance.currentHandle,
            (instance, value) => instance.currentHandle = value);


    // Binding redraw triggering control properties 
    public byte[]? Board
    {
        get => board;
        set
        {
            boardCache = null;
            SetAndRaise(BoardProperty, ref board, value);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    public byte[]? Changes
    {
        get => changes;
        set
        {
            changesCache = null;
            SetAndRaise(ChangesProperty, ref changes, value);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }
    
    public byte[]? SelectionBoard
    {
        get => selectionBoard;
        set
        {
            selectionCanvasCache = null;
            SetAndRaise(SelectionBoardProperty, ref selectionBoard, value);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }
    
    public uint CanvasWidth
    {
        get => canvasWidth;
        set => SetAndRaise(CanvasWidthProperty, ref canvasWidth, value);
    }
    
    public uint CanvasHeight
    {
        get => canvasHeight;
        set => SetAndRaise(CanvasHeightProperty, ref canvasHeight, value);
    }
    
    public Selection? CurrentSelection
    {
        get => currentSelection;
        set
        {
            SetAndRaise(CurrentSelectionProperty, ref currentSelection, value);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    public List<Selection> Selections
    {
        get => selections;
        set => SetAndRaise(SelectionsProperty, ref selections, value);
    }

    public SelectionHandle CurrentHandle
    {
        get => currentHandle;
        set => SetAndRaise(CurrentHandleProperty, ref currentHandle, value);
    }

    // Non-binding redraw triggering control properties 
    public byte[]? SocketPixels
    {
        get => socketPixels;
        set
        {
            socketPixels = value;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    public float Top
    {
        get => canvPosition.Y;
        set
        {
            canvPosition = new SKPoint(canvPosition.X, value);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        } 
    }
    public float Left
    {
        get => canvPosition.X;
        set
        {
            canvPosition = new SKPoint(value, canvPosition.Y);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }
    public float Zoom
    {
        get => canvZoom;
        set
        {
            canvZoom = value;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    public SkCanvas()
    {
        InitializeComponent();
        
        ClipToBounds = true;
        foreach (var col in PaletteViewModel.Colours)
        {
            paints.Add(new SKPaint { Color = col });
        }
    }

    private class CustomDrawOp : ICustomDrawOperation
    {
        private readonly SkCanvas parent;
        public Rect Bounds { get; }

        public CustomDrawOp(Rect bounds, SkCanvas parentSk)
        {
            Bounds = bounds;
            parent = parentSk;
        }

        public void Dispose() { }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        
        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
            {
                Console.WriteLine("[ERROR] Could not get ISkiaSharpApiLeaseFeature feature. Perhaps rendering backend is not skia?");
                return;
            }
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            
            //These must happen first because apparently it just works that way, idk.
            canvas.Scale(parent.canvZoom, parent.canvZoom);
            canvas.Translate(parent.canvPosition.X, parent.canvPosition.Y);

            //Equivalent of renderAll
            if (parent.boardCache is null && parent.Board is not null)
            {
                using var img = new SKBitmap((int)parent.CanvasWidth, (int)parent.CanvasHeight, true);
                for (var i = 0; i < Math.Min(parent.Board.Length, parent.CanvasWidth * parent.CanvasHeight); i++)
                {
                    var colourI = parent.Board[i];
                    if (colourI < PaletteViewModel.Colours.Length)
                    {
                        img.SetPixel(
                            (int)(i % parent.CanvasWidth),
                            (int)(i / parent.CanvasWidth),
                            PaletteViewModel.Colours[colourI]);
                    }
                }

                parent.boardCache = SKImage.FromBitmap(img);
            }
            if (parent.changesCache is null && parent.Changes is not null)
            {
                var canvasWidth = (int) parent.CanvasWidth;
                var canvasHeight = (int) parent.CanvasHeight;
                using var img = new SKBitmap(canvasWidth, canvasHeight);
                for (var i = 0; i < Math.Min(parent.Changes.Length, canvasWidth * canvasHeight); i++)
                {
                    if (parent.Changes[i] == 0 || parent.Changes[i] > PaletteViewModel.Colours.Length - 1)
                    {
                        continue;
                    }
                    img.SetPixel((int)(i % parent.CanvasWidth), (int)(i / parent.CanvasWidth),
                        PaletteViewModel.Colours[parent.Changes[i]]);
                }
                
                parent.changesCache = SKImage.FromBitmap(img);
            }

            if (parent.boardCache is not null)
            {
                canvas.DrawImage(parent.boardCache, 0, 0);
                
                if (parent.changesCache is not null)
                {
                    canvas.DrawImage(parent.changesCache, 0, 0);
                }
            }
            else
            {
                //Draw rplacetk logo background instead
                var bck = new SKPaint { Color = new SKColor(51, 51, 51, 100) };
                var frg = new SKPaint { Color = new SKColor(255, 87, 0, 200) };
                var dot = new SKPaint { Color = SKColors.Black };
                canvas.DrawRect(0, 0, 500, 500, bck); //background
                canvas.DrawRect(74, 74, 280, 70, frg); //top
                canvas.DrawRect(74, 144, 70, 280, frg); //left
                canvas.DrawRect(354, 144, 70, 280, frg); //right
                canvas.DrawRect(214, 354, 140, 70, frg); //bottom
                canvas.DrawRect(214, 214, 72, 72, dot); //centre
            }
            
            //Draw all pixels that have come in to the canvas.
            if (parent.socketPixels is not null)
            {
                for (var c = 0; c < parent.socketPixels.Length; c++)
                {
                    if (parent.socketPixels[c] == 255) continue;
                    canvas.DrawRect(c % parent.CanvasWidth, c / parent.CanvasWidth, 1, 1, parent.paints[parent.socketPixels[c]]);
                }
            }
            
            if (parent.SelectionBoard is not null && parent.selectionCanvasCache is null)
            {
                using var img = new SKBitmap((int)parent.CanvasWidth, (int)parent.CanvasHeight);
                var drawnPixel = false;
                lock (parent.selectionsLock)
                {
                    for (var i = 0; i < parent.SelectionBoard.Length; i++)
                    {
                        //TODO: This method is not fully efficient and only attempting to draw at all within the selection bounds would be better.
                        foreach (var sel in parent.Selections)
                        {
                            if (drawnPixel)
                            {
                                continue;
                            }
                            
                            if (i % parent.CanvasWidth >= sel.TopLeft.X
                                && i % parent.CanvasWidth <= sel.BottomRight.X
                                && i / parent.CanvasHeight >= sel.TopLeft.Y
                                && i / parent.CanvasHeight <= sel.BottomRight.Y)
                            {
                                img.SetPixel((int)(i % parent.CanvasWidth), (int)(i / parent.CanvasWidth), PaletteViewModel.Colours[parent.SelectionBoard[i]]);
                                drawnPixel = true;
                            }
                        }
                        
                        drawnPixel = false;
                    }
                }

                parent.selectionCanvasCache = SKImage.FromBitmap(img);
            }
            if (parent.selectionCanvasCache is not null)
            {
                canvas.DrawImage(parent.selectionCanvasCache, 0, 0);
            }
            
            //Draw selections
            foreach (var sel in parent.Selections)
            {
                var sKBrush = new SKPaint
                {
                    Color = new SKColor(100, 167, 255, 140)
                };
                var selX = (float) Math.Floor(sel.TopLeft.X);
                var selY = (float) Math.Floor(sel.TopLeft.Y);
                var selWidth = (float) Math.Floor(sel.BottomRight.X - selX);
                var selHeight = (float) Math.Floor(sel.BottomRight.Y - selY);
                canvas.DrawRect(selX, selY, selWidth, selHeight, sKBrush);

                using var selInfoPaint = new SKPaint()
                {
                    TextSize = 18,
                    Color = SKColors.White,
                    IsAntialias = true
                };
                using var selInfoOutlinePaint = new SKPaint()
                {
                    TextSize = 18,
                    Color = SKColors.Black,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 2
                };
                canvas.DrawText($"({selX}, {selY}) {selWidth}x{selHeight}px", selX, selY, selInfoOutlinePaint);
                canvas.DrawText($"({selX}, {selY}) {selWidth}x{selHeight}px", selX, selY, selInfoPaint);

                if (sel == parent.currentSelection)
                {
                    using var selHandlePoint = new SKPaint
                    {
                        Color = SKColors.Blue
                    };
                    
                    canvas.DrawCircle(new SKPoint((float) sel.TopLeft.X, (float) sel.TopLeft.Y), 4, selHandlePoint);
                    canvas.DrawCircle(new SKPoint((float) sel.TopLeft.X, (float) sel.BottomRight.Y), 4, selHandlePoint);
                    canvas.DrawCircle(new SKPoint((float) sel.BottomRight.X, (float) sel.TopLeft.Y), 4, selHandlePoint);
                    canvas.DrawCircle(new SKPoint((float) sel.BottomRight.X, (float) sel.BottomRight.Y), 4, selHandlePoint);
                }
            }
            
            canvas.Flush();
            canvas.Restore();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new CustomDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height), this));
    }
    
    public Selection StartSelection(Point topLeft, Point bottomRight)
    {
        var sel = new Selection
        {
            TopLeft = topLeft,
            BottomRight = bottomRight
        };

        lock (selectionsLock)
        {
            Selections.Add(sel);
        }
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        return sel;
    }

    public void UpdateSelection(Selection selection, Point? topLeft = null, Point? bottomRight = null)
    {
        selection.TopLeft = topLeft ?? selection.TopLeft;
        selection.BottomRight = bottomRight ?? selection.BottomRight;
        selectionCanvasCache = null;
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    public void RemoveSelection(Selection selection)
    {
        lock (selectionsLock)
        {
            Selections.Remove(selection);
        }

        selectionCanvasCache = null;
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    public void ClearSelections()
    {
        lock (selectionsLock)
        {
            Selections.Clear();
        }

        selectionCanvasCache = null;
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }
    
    public void Set(Pixel pixel)
    {
        if (socketPixels is null)
        {
            socketPixels = new byte[CanvasWidth * CanvasHeight];
            Array.Fill(socketPixels, (byte) 255);
        }

        if (pixel.Index < socketPixels.Length)
        {
            socketPixels[pixel.Index] = pixel.Colour;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }
    
    public void Unset(int x, int y)
    {
        if (socketPixels is null)
        {
            return;
        }

        socketPixels[x + y * CanvasWidth] = 255;
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }
}
