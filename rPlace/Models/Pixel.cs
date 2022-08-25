using System;
using System.IO;
using System.Text;

namespace rPlace.Models;

public class Pixel
{
    public int Index => X % Width + (Y % Height) * Width; //board[x % canvas.width + (y % canvas.height) * canvas.width] = b
    public int Colour { get; }
    private int X { get; }
    private int Y { get; }
    private  int Width { get; }
    private int Height { get;  }

    public Pixel(int colour, int x, int y, int canvWidth, int canvHeight)
    {
        Colour = colour;
        X = x;
        Y = y;
        Width = canvWidth;
        Height = canvHeight;
    }


    //First byte is the code, pixel place is code for, uint32 is 
    public byte[] ToByteArray()
    { 
        using var writer = new BinaryWriter(new MemoryStream(), Encoding.Default);
        writer.Write(BitConverter.GetBytes((byte) 4));
        writer.Write(BitConverter.GetBytes((uint) X + Y * Width));
        writer.Write(BitConverter.GetBytes((byte) Colour));
        return ((MemoryStream) writer.BaseStream).ToArray();
    }
    
    public void Deconstruct(out int c, out int x, out int y)
    {
        c = Colour;
        x = X;
        y = Y;
    }
}