using System;
using System.Buffers.Binary;
using System.Text;

namespace RplaceModtools.Models;

public struct Pixel
{
    public byte Colour;
    public uint Index; 
    
    public Pixel(int index, byte colour)
    {
        Index = (uint) index;
        Colour = colour;
    }
    
    public Pixel(uint index, byte colour)
    {
        Index = index;
        Colour = colour;
    }

    public Pixel(int x, int y, uint canvasWidth, byte colour)
    {
        Index = (uint)(x + y * canvasWidth);
        Colour = colour;
    }
    
    public Pixel(uint x, uint y, uint canvasWidth, byte colour)
    {
        Index = x + y * canvasWidth;
        Colour = colour;
    }

    public (uint X, uint Y) GetPosition(uint canvasWidth)
    {
        return (Index % canvasWidth, Index / canvasWidth);
    }

    public void SetPosition(uint x, uint y, uint canvasWidth)
    {
        Index = x + y * canvasWidth;
    }
    
    public void SetPosition(int x, int y, uint canvasWidth)
    {
        Index = (uint)(x + y * canvasWidth);
    }

    //First byte is the code, pixel place is code for, uint32 is 
    public byte[] ToByteArray()
    {
        var buffer = new byte[6];
        buffer[0] = 4;
        BinaryPrimitives.TryWriteUInt32BigEndian(buffer.AsSpan()[1..], (uint) Index);
        buffer[5] = Colour;
        return buffer;
    }

    public Pixel Clone()
    {
        return new Pixel(Index, Colour);
    }
}