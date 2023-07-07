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
    public Stack<Selection> Selections = new();
    
    private static byte[]? board;
    private static byte[]? changes;
    private static SKImage? boardCache;
    private static SKImage? changesCache;
    private static byte[]? socketPixels;

    private static byte[]? selectionBoard;
    private static SKImage? selectionCanvasCache;
    private static float canvZoom = 1;
    private static SKPoint canvPosition = SKPoint.Empty;
    private static List<SKPaint> paints = new();

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
        
        public void Render(IDrawingContextImpl context)
        {
            //Console.WriteLine("Drawing " + DateTime.Now + " " + Thread.GetCurrentProcessorId());
            var canvas = (context as ISkiaDrawingContextImpl)?.SkCanvas;
            if (canvas == null)
            {
                throw new Exception("[Fatal] Render Error: SkCanvas was null, perhaps not using skia as render backend?");
            }
            canvas.Save();
            
            //These must happen first because apparently it just works that way, idk.
            canvas.Scale(canvZoom,canvZoom);
            canvas.Translate(canvPosition.X, canvPosition.Y);

            //Equivalent of renderAll
            if (boardCache is null && board is not null)
            {
                using var img = new SKBitmap((int)ParentSk.CanvasWidth, (int)ParentSk.CanvasHeight, true);
                for (var i = 0; i < board.Length; i++)
                {
                    img.SetPixel((int)(i % ParentSk.CanvasWidth), (int)(i / ParentSk.CanvasWidth), PaletteViewModel.Colours[board[i]]);
                }

                boardCache = SKImage.FromBitmap(img);
            }
            if (changesCache is null && changes is not null)
            {
                using var img = new SKBitmap((int)ParentSk.CanvasWidth, (int)ParentSk.CanvasHeight, true);
                for (var i = 0; i < changes.Length; i++)
                {
                    img.SetPixel((int)(i % ParentSk.CanvasWidth), (int)(i / ParentSk.CanvasWidth), PaletteViewModel.Colours[changes[i]]);
                }
                
                changesCache = SKImage.FromBitmap(img);
            }
            
            if (changesCache is not null && boardCache is not null)
            {
                canvas.DrawImage(changesCache, 0, 0);
                canvas.DrawImage(boardCache, 0, 0);
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
            if (socketPixels is not null)
            {
                for (var c = 0; c < socketPixels.Length; c++)
                {
                    if (socketPixels[c] == 255) continue;
                    canvas.DrawRect(c % ParentSk.CanvasWidth, c / ParentSk.CanvasWidth, 1, 1, paints[socketPixels[c]]);
                }
            }
            
            if (selectionBoard is not null && selectionCanvasCache is null)
            {
                using var img = new SKBitmap((int)ParentSk.CanvasWidth, (int)ParentSk.CanvasHeight);
                for (var i = 0; i < selectionBoard.Length; i++)
                {
                    //TODO: This method is not fully efficient and only attempting to draw at all within the selection bounds would be better.
                    foreach (var sel in ParentSk.Selections
                        .Where(sel => i % (ParentSk.CanvasWidth) >= sel.TopLeft.X
                            && i % ParentSk.CanvasWidth <= sel.BottomRight.X
                            && i / ParentSk.CanvasHeight >= sel.TopLeft.Y
                            && i / ParentSk.CanvasHeight <= sel.BottomRight.Y))
                    {
                        img.SetPixel((int)(i % ParentSk.CanvasWidth), (int)(i / ParentSk.CanvasWidth), PaletteViewModel.Colours[selectionBoard[i]]);
                    }
                }
                selectionCanvasCache = SKImage.FromBitmap(img);
                selectionBoard = null;
                img.Dispose();
            }
            if (selectionCanvasCache is not null)
            {
                canvas.DrawImage(selectionCanvasCache, 0, 0);
            }
            
            //Draw selections
            foreach (var sel in ParentSk.Selections)
            {
                var sKBrush = new SKPaint();
                sKBrush.Color = new SKColor(100, 167, 255, 140);
                canvas.DrawRect((float) Math.Floor(sel.TopLeft.X), (float) Math.Floor(sel.TopLeft.Y), (float) Math.Floor(sel.BottomRight.X), (float) Math.Floor(sel.BottomRight.Y), sKBrush);
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
    
    public void StartSelection(Point topLeft, Point bottomRight)
    {
        var sel = new Selection
        {
            TopLeft = topLeft,
            BottomRight = bottomRight
        };
        Selections.Push(sel);
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateSelection(Point? topLeft = null, Point? bottomRight = null)
    {
        var cur = Selections.Pop();
        cur.TopLeft = topLeft ?? cur.TopLeft;
        cur.BottomRight = bottomRight ?? cur.BottomRight;
        Selections.Push(cur);
        selectionCanvasCache = null;
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    public void ClearSelections()
    {
        Selections = new Stack<Selection>();
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
        
        socketPixels[pixel.Index] = pixel.Colour;
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
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

public struct Selection
{
    public Point TopLeft;
    public Point BottomRight;
}
