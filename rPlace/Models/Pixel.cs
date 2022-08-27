using System;
using System.Buffers.Binary;
using System.Text;

namespace rPlace.Models;

public class Pixel
{
    public int Index => X % Width + (Y % Height) * Width;
    public int Colour { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public  int Width { get; init; }
    public int Height { get; init; }

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

    public Pixel Clone() => (Pixel) MemberwiseClone();
}