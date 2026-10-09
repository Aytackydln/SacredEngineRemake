namespace Sacred.World.Particles;

public readonly record struct ParticleCornerColors(uint Color0, uint Color1, uint Color2, uint Color3)
{
    public uint this[int index] => index switch
    {
        0 => Color0, 1 => Color1, 2 => Color2, 3 => Color3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}
