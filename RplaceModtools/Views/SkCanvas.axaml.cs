using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
    public uint CanvasWidth = 500;
    public uint CanvasHeight = 500;
    public List<Selection> Selections = new(); // TODO: ReadonlyList
    public SelectionHandle CurrentHandle = SelectionHandle.None;
    
    protected Selection? currentSelection;
    protected byte[]? board;
    protected byte[]? changes;
    protected SKImage? boardCache;
    protected SKImage? changesCache;
    protected byte[]? socketPixels;

    protected byte[]? selectionBoard;
    protected SKImage? selectionCanvasCache;
    protected float canvZoom = 1;
    protected SKPoint canvPosition = SKPoint.Empty;
    protected List<SKPaint> paints = new();

    public byte[]? SocketPixels
    {
        get => socketPixels;
        set
        {
            socketPixels = value;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }
    
    public byte[]? Board
    {
        get => board;
        set
        {
            // We have to invalidate the caches so that a new one can be generated
            boardCache = null;
            board = value;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    public byte[]? Changes
    {
        get => changes;
        set
        {
            // We have to invalidate the caches so that a new one can be generated
            changesCache = null;
            changes = value;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    public byte[]? SelectionBoard
    {
        get => selectionBoard;
        set
        {
            selectionBoard = value;
            selectionCanvasCache = null;
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

    public Selection? CurrentSelection
    {
        get => currentSelection;
        set
        {
            currentSelection = value;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }


    public SkCanvas()
    {
        InitializeComponent();
        ClipToBounds = true;
        foreach (var col in PaletteViewModel.Colours) paints.Add(new SKPaint { Color = col });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private class CustomDrawOp : ICustomDrawOperation
    {
        private SkCanvas ParentSk { get; }

        public Rect Bounds { get; }

        public CustomDrawOp(Rect bounds, SkCanvas parentSk)
        {
            Bounds = bounds;
            ParentSk = parentSk;
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
            canvas.Scale(ParentSk.canvZoom, ParentSk.canvZoom);
            canvas.Translate(ParentSk.canvPosition.X, ParentSk.canvPosition.Y);

            //Equivalent of renderAll
            if (ParentSk.boardCache is null && ParentSk.board is not null)
            {
                using var img = new SKBitmap((int)ParentSk.CanvasWidth, (int)ParentSk.CanvasHeight, true);
                for (var i = 0; i < ParentSk.board.Length; i++)
                {
                    var colourI = ParentSk.board[i];
                    if (colourI < PaletteViewModel.Colours.Length)
                    {
                        img.SetPixel((int)(i % ParentSk.CanvasWidth), (int)(i / ParentSk.CanvasWidth), PaletteViewModel.Colours[colourI]);
                    }
                }

                ParentSk.boardCache = SKImage.FromBitmap(img);
            }
            if (ParentSk.changesCache is null && ParentSk.changes is not null)
            {
                using var img = new SKBitmap((int)ParentSk.CanvasWidth, (int)ParentSk.CanvasHeight);
                for (var i = 0; i < ParentSk.changes.Length; i++)
                {
                    if (ParentSk.changes[i] == 0)
                    {
                        continue;
                    }
                    
                    img.SetPixel((int)(i % ParentSk.CanvasWidth), (int)(i / ParentSk.CanvasWidth), PaletteViewModel.Colours[ParentSk.changes[i]]);
                }
                
                ParentSk.changesCache = SKImage.FromBitmap(img);
            }

            if (ParentSk.boardCache is not null)
            {
                canvas.DrawImage(ParentSk.boardCache, 0, 0);
                
                if (ParentSk.changesCache is not null)
                {
                    canvas.DrawImage(ParentSk.changesCache, 0, 0);
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
            if (ParentSk.socketPixels is not null)
            {
                for (var c = 0; c < ParentSk.socketPixels.Length; c++)
                {
                    if (ParentSk.socketPixels[c] == 255) continue;
                    canvas.DrawRect(c % ParentSk.CanvasWidth, c / ParentSk.CanvasWidth, 1, 1, ParentSk.paints[ParentSk.socketPixels[c]]);
                }
            }
            
            if (ParentSk.selectionBoard is not null && ParentSk.selectionCanvasCache is null)
            {
                using var img = new SKBitmap((int)ParentSk.CanvasWidth, (int)ParentSk.CanvasHeight);
                for (var i = 0; i < ParentSk.selectionBoard.Length; i++)
                {
                    //TODO: This method is not fully efficient and only attempting to draw at all within the selection bounds would be better.
                    foreach (var sel in ParentSk.Selections
                        .Where(sel => i % (ParentSk.CanvasWidth) >= sel.TopLeft.X
                            && i % ParentSk.CanvasWidth <= sel.BottomRight.X
                            && i / ParentSk.CanvasHeight >= sel.TopLeft.Y
                            && i / ParentSk.CanvasHeight <= sel.BottomRight.Y))
                    {
                        img.SetPixel((int)(i % ParentSk.CanvasWidth), (int)(i / ParentSk.CanvasWidth), PaletteViewModel.Colours[ParentSk.selectionBoard[i]]);
                    }
                }

                ParentSk.selectionCanvasCache = SKImage.FromBitmap(img);
            }
            if (ParentSk.selectionCanvasCache is not null)
            {
                canvas.DrawImage(ParentSk.selectionCanvasCache, 0, 0);
            }
            
            //Draw selections
            foreach (var sel in ParentSk.Selections)
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

                if (sel == ParentSk.currentSelection)
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
        
        Selections.Add(sel);
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
        Selections.Remove(selection);
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    public void ClearSelections()
    {
        Selections.Clear();
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

public class Selection
{
    public Point TopLeft;
    public Point BottomRight;
}
