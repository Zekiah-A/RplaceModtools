using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using rPlace.Models;
using rPlace.ViewModels;
using SkiaSharp;
using Point = Avalonia.Point;

namespace rPlace.Views;

public partial class SkCanvas : UserControl
{
    public int? CanvasWidth = 500;
    public int? CanvasHeight = 750;
    public Stack<Selection> Selections = new();
    
    private static byte[]? board;
    private static char[]? changes;
    private static byte[]? selectionBoard;
    private static List<Pixel> pixelsToDraw = new();
    private static SKImage? canvasCache;
    private static SKImage? selectionCanvasCache;
    private static float canvZoom = 1;
    private static SKPoint canvPosition = SKPoint.Empty;
    private static SKColor? pixelAtColour;
    private static Vector2? pixelAtPosition;
    private static Stopwatch? stopwatch;

    public byte[]? Board
    {
        get => board;
        set
        {
            board = value;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
    }

    public char[]? Changes
    {
        get => changes;
        set
        {
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
        //canvDrawOp = new CustomDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    class CustomDrawOp : ICustomDrawOperation
    {
        private SkCanvas ParentSk { get; }

        public CustomDrawOp(Rect bounds, SkCanvas parentSk)
        {
            Bounds = bounds;
            ParentSk = parentSk;
        }
        
        public void Dispose() { }
        
        public Rect Bounds { get; set; }
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        
        public void Render(IDrawingContextImpl context)
        {
            //Console.WriteLine("Drawing " + DateTime.Now + " " + Thread.GetCurrentProcessorId());
            var canvas = (context as ISkiaDrawingContextImpl)?.SkCanvas;
            if (canvas == null) throw new Exception("[Fatal] Render Error: SkCanvas was null, perhaps not using skia as render backend?");
            canvas.Save();
            
            //These must happen first because apparently it just works that way, idk.
            canvas.Scale(canvZoom,canvZoom);
            canvas.Translate(canvPosition.X, canvPosition.Y);

            //Equivalent of renderAll
            if (board is not null)
            {
                using var img = new SKBitmap(ParentSk.CanvasWidth ?? 500, ParentSk.CanvasHeight ?? 500, true);
                for (var i = 0; i < board.Length; i++)
                    img.SetPixel(i % ParentSk.CanvasWidth ?? 500, i / ParentSk.CanvasWidth ?? 500, PaletteViewModel.Colours[board[i]]);
                canvasCache = SKImage.FromBitmap(img);
                img.Dispose();
                board = null;
            }
            if (canvasCache is null)
            {
                //Draw rplacetk logo background instead
                var bck = new SKPaint(); bck.Color = new SKColor(51, 51, 51, 100);
                var frg = new SKPaint(); frg.Color = new SKColor(255, 87, 0, 200);
                var dot = new SKPaint(); dot.Color = SKColors.Black;
                canvas.DrawRect(0, 0, 500, 500, bck); //background
                canvas.DrawRect(74, 74, 280, 70, frg); //top
                canvas.DrawRect(74, 144, 70, 280, frg); //left
                canvas.DrawRect(354, 144, 70, 280, frg); //right
                canvas.DrawRect(214, 354, 140, 70, frg); //bottom
                canvas.DrawRect(214, 214, 72, 72, dot); //centre
            }
            else canvas.DrawImage(canvasCache, 0, 0);
            
            //Draw all pixels that have come in to the canvas.
            foreach (var (c, x, y) in pixelsToDraw) canvas.DrawRect(x, y, 1, 1, new SKPaint {Color = PaletteViewModel.Colours[c]});

            if (selectionBoard is not null && selectionCanvasCache is null)
            {
                using var img = new SKBitmap(ParentSk.CanvasWidth ?? 500, ParentSk.CanvasHeight ?? 500);
                for (var i = 0; i < selectionBoard.Length; i++)
                {   //TODO: This method is not fully efficient and only attempting to draw at all within the selection bounds would be better.
                    foreach (var sel in ParentSk.Selections)
                    {   //Loop through the whole board, if pixel is within bounds of selection, draw, otherwise skip.
                        if (i % (ParentSk.CanvasWidth ?? 500) >= sel.Tl.X && i % (ParentSk.CanvasWidth ?? 500) <= sel.Br.X && i / (ParentSk.CanvasHeight ?? 500) >= sel.Tl.Y && i / (ParentSk.CanvasHeight ?? 500) <= sel.Br.Y)
                            img.SetPixel(i % ParentSk.CanvasWidth ?? 500, i / ParentSk.CanvasHeight ?? 500, PaletteViewModel.Colours[selectionBoard[i]]);
                    }
                }
                selectionCanvasCache = SKImage.FromBitmap(img);
                img.Dispose();
            }
            if (selectionCanvasCache is not null) canvas.DrawImage(selectionCanvasCache, 0, 0);
            
            //Draw selections
            foreach (var sel in ParentSk.Selections)
            {
                var sKBrush = new SKPaint();
                sKBrush.Color = new SKColor(100, 167, 255, 140);
                canvas.DrawRect((float) Math.Floor(sel.Tl.X), (float) Math.Floor(sel.Tl.Y), (float) Math.Floor(sel.Br.X), (float) Math.Floor(sel.Br.Y), sKBrush);
            }
            
            //Get pixel colour at screen co-ordinate, to return to ColourAt
            if (pixelAtPosition is not null)
            {
                var dstinf = new SKImageInfo
                {
                    ColorType = SKColorType.Rgb888x,
                    AlphaType = SKAlphaType.Opaque,
                    Width = 1,
                    Height = 1
                };
                var bitmap = new SKBitmap(dstinf);
                var dstpixels = bitmap.GetPixels();
                (context as ISkiaDrawingContextImpl)?.SkSurface.ReadPixels(dstinf, dstpixels, dstinf.RowBytes, (int) pixelAtPosition.Value.X, (int) pixelAtPosition.Value.Y);
                pixelAtColour = bitmap.GetPixel(0, 0);
                pixelAtPosition = null;
            }
            canvas.Flush();
            canvas.Restore();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        //TODO: Making a new instance of the CanvdDrawOp each time is really inefficient, but so far it is the only found way to force a redraw on the render thread.
        using var canvDrawOp = new CustomDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height), this); //Let's disable the global customdrawop for now until we fix above todo
        context.Custom(canvDrawOp);
    }

    //Decompress changes so it can be put onto canv
    public void RunLengthChanges(char[] data)
    {
        int i = 9, boardI = 0;
        UInt32 w = data[1], h = data[5];
        //board8 = new char[250000];
        while (i < data.Length)
        {
            var cell = data[i++];
            var c = cell >> 6;
            if (c == 1) c = c = data[i++];
            else if (c == 2)
            {
                c = data[i++];
                i++;
            }
            else if (c == 3)
            {
                c = data[i++];
                i += 3;
            }
            boardI += c;
            //board8[boardI++] = (char) (cell & 63);
        }
    }

    public void StartSelection(Point topLeft, Point bottomRight)
    {
        var sel = new Selection
        {
            Tl = topLeft,
            Br = bottomRight
        };
        Selections.Push(sel);
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    public void UpdateSelection(Point? topLeft = null, Point? bottomRight = null)
    {
        var cur = Selections.Pop();
        cur.Tl = topLeft ?? cur.Tl;
        cur.Br = bottomRight ?? cur.Br;
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
    
    public SKColor? ColourAt(int x, int y)
    {
        //Limit the amount of times we re-render to get the colour to 60fps framerate.
        if (stopwatch is null) { stopwatch = new(); stopwatch.Start(); return null; } 
        if (stopwatch.ElapsedMilliseconds < 16) return null;
        pixelAtPosition = new Vector2(x, y);
        VisualRoot?.Renderer.AddDirty(this);
        stopwatch.Restart();
        return pixelAtColour;
    }

    public void Set(Pixel pixel)
    {
        pixelsToDraw.Add(pixel);
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }
}

public struct Selection
{
    public Point Tl;
    public Point Br;
}
