namespace Sacred.World.Rendering;

/// <summary>An uncompressed, top-down RGBA image shared by headless world renderers.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels)
{
    public int Stride => checked(Width * 4);

    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        if (Pixels.Length != checked(Width * Height * 4))
            throw new InvalidDataException($"Expected {Width * Height * 4} RGBA bytes, found {Pixels.Length}.");
    }
}
