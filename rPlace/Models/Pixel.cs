using System;
using System.Buffers.Binary;
using System.Text;

namespace rPlace.Models;

public class Pixel
{
    public int Index
    {
        get => X % Width + Y % Height * Width;
        init
        {
            X = value % Width;
            Y = value / Width;
        }
    }

    public int Colour { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    public  int Width { get; init; }
    public int Height { get; init; }

    //First byte is the code, pixel place is code for, uint32 is 
    public byte[] ToByteArray()
    {
        var buffer = new byte[6]; //var span = new Span<byte>(buffer); span.Slice(0, ...)
        buffer[0] = (byte) 4; //BitConverter.TryWriteBytes(span[0..], (byte) 4);
        BinaryPrimitives.TryWriteUInt32BigEndian(buffer.AsSpan()[1..], (uint) Index);
        buffer[5] = (byte) Colour;//BitConverter.TryWriteBytes(span[5..], (byte) Colour);
        return buffer; //span.ToArray();
    }

    public void Deconstruct(out int c, out int x, out int y)
    {
        c = Colour;
        x = X;
        y = Y;
    }

    public Pixel Clone() => (Pixel) MemberwiseClone();
}