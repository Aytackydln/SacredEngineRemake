namespace Sacred.World.Particles;

public readonly record struct ParticleCornerColors(uint Color0, uint Color1, uint Color2, uint Color3)
{
    public uint this[int index] => index switch
    {
        0 => Color0, 1 => Color1, 2 => Color2, 3 => Color3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    /// <summary>Native triangle strip Gouraud interpolation. Texture V runs top to bottom;
    /// corners 0/2 are the bottom edge, and the shared diagonal joins corners 1/2.</summary>
    public float Channel(float u, float v, int shift)
    {
        float C(uint color) => (color >> shift) & 255;
        return u <= v
            ? C(Color0) * (v - u) + C(Color1) * (1 - v) + C(Color2) * u
            : C(Color1) * (1 - u) + C(Color2) * v + C(Color3) * (u - v);
    }
}
