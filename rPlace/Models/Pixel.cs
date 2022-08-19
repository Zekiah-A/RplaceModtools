using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Avalonia.Controls.Platform;
using SkiaSharp;

namespace rPlace.Models;

public class Pixel
{
    private int Colour { get; }
    private int X { get; }
    private int Y { get; }
    private  int Width { get; }

    public Pixel(int colour, int x, int y, int canvWidth)
    {
        Colour = colour;
        X = x;
        Y = y;
        Width = canvWidth;
    }


    //First byte is the code, pixel place is code for, uint32 is 
    public byte[] ToByteArray()
    { 
        using var writer = new BinaryWriter(new MemoryStream(), Encoding.UTF8);
        writer.Write((byte) 0);
        writer.Write(BitConverter.GetBytes((byte) 4));
        writer.Write(BitConverter.GetBytes((uint) X * Y * Width));
        writer.Write((byte) Colour);
        return ((MemoryStream) writer.BaseStream).ToArray();
    }

    public void Deconstruct(out int c, out int x, out int y)
    {
        c = Colour;
        x = X;
        y = Y;
    }
}