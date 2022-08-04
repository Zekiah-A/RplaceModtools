using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using rPlace.Models;
using SkiaSharp;
using Point = Avalonia.Point;

namespace rPlace.Views;

public partial class SkCanvas : UserControl
{
    //Board and changes are cached to improve performance as they only need to be processed once.
    private static byte[]? board;
    public static char[]? changes;
    private static Stack<Selection> selections = new();
    private static Stack<byte[]> pixelsToDraw = new();
    //private CustomDrawOp canvDrawOp; //see line 172
    private static SKImage? canvasCache;
    private static float canvZoom = 1;
    private static SKPoint canvPosition = new SKPoint(0, 0);

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
            //TODO: Custom draw doesn't run on UI thread, but render thread, all this invalidatevisual is useless, i JUST NEED OT GET ONTO THE DAN RENDER THREAD TO CALL IT http://reference.avaloniaui.net/api/Avalonia.Rendering/DeferredRenderer/
            //https://github.com/wieslawsoltes/Draw2D/blob/509d26f132039f9a97f28088491e1da849b5d2a2/src/Draw2D/Editor/SkiaShapeRenderer.cs#L13-L380
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
        public CustomDrawOp(Rect bounds)
        {
            Bounds = bounds;
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
                using var img = new SKBitmap(500, 500, true);
                for (var i = 0; i < board.Length; i++)
                    img.SetPixel(i % 500, i / 500, Utils.PColours[board[i]]);
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
            
            //Equivalent of seti, we go through each pixel that needs to be added for this frame, popping them all off.
            while (pixelsToDraw.Count > 0)
            {
                var p = pixelsToDraw.Pop();
                var c = new SKPaint();
                c.Color = Utils.PColours[BitConverter.ToUInt32(p, 5)]; //0 is code, 1-4 is cooldown, 5 is colour, 9 is position
                canvas.DrawRect(BitConverter.ToUInt32(p, 9) % 500, (float) Math.Floor(BitConverter.ToUInt32(p, 9) / 500f), 1, 1, c);
            }

            //Draw selections
            foreach (var sel in selections)
            {
                var sKBrush = new SKPaint();
                sKBrush.Color = new SKColor(100, 167, 255, 140);
                canvas.DrawRect((float)sel.Tl.X, (float)sel.Tl.Y, (float)sel.Br.X, (float)sel.Br.Y, sKBrush);
            }
            
            canvas.Flush();
            canvas.Restore();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        //TODO: Making a new instance of the CanvdDrawOp each time is really inefficient, but so far it is the only found way to force a redraw on the render thread.
        using var canvDrawOp = new CustomDrawOp(new Rect(0, 0, Bounds.Width, Bounds.Height)); //Let's disable the global customdrawop for now until we fix above todo
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
        selections.Push(sel);
    }

    public void UpdateSelection(Point topLeft, Point bottomRight)
    {
        var cur = selections.Pop();
        cur.Tl = topLeft;
        cur.Br = bottomRight;
        selections.Push(cur);
    }

    public void ClearSelections() => selections = new Stack<Selection>();
}

public struct Selection
{
    public Point Tl;
    public Point Br;
}
