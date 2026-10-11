namespace Sacred.Particles;

/// <summary>Native initializers copy the mapped event colour to all four fixed corner colours.</summary>
public static class SacredParticleEventColorParameters
{
    public static SacredParticleDefinition Resolve(SacredParticleDefinition definition, uint? color)
    {
        if (color is null) return definition;
        if (!definition.UsesEventCornerColor)
            throw new ArgumentException("This particle recipe has no decoded event corner-colour input.", nameof(color));
        return definition with
        {
            ParameterSets = definition.ParameterSets.Select(p => p with
            {
                Colors = Array.AsReadOnly(Enumerable.Repeat(color.Value & definition.EventCornerColorMask, 4).ToArray())
            }).ToArray()
        };
    }
}
