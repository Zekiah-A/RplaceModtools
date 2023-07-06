namespace RplaceModtools.Models;

public record UnpackedBoard(byte[] Board, int Width, List<uint> Palette);