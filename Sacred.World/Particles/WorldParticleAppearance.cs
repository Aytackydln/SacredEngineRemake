using Sacred.Particles;

namespace Sacred.World.Particles;

internal static class WorldParticleAppearance
{
    public static ParticleCornerColors? Corners(SacredParticleDrawDefinition draw, SacredParticleParameterSet parameters, float fade)
    {
        var colors = parameters.Colors;
        if (draw.UsesColorTable || draw.UsesParticleColor || colors.Count != 4 || colors.All(c => c == colors[0])) return null;
        var alpha = (draw.RawFlags & 2) != 0 ? (uint)Math.Clamp((int)fade, 0, 255) << 24 : 0;
        return new(colors[0] | alpha, colors[1] | alpha, colors[2] | alpha, colors[3] | alpha);
    }

    public static uint Color(SacredParticleDrawDefinition draw, SacredParticleParameterSet parameters, float fade)
    {
        var index = Math.Clamp((int)fade, 0, 255);
        if (draw.UsesColorTable)
            return parameters.Colors[index];
        var rgb = parameters.Colors.Count == 0 ? parameters.Emission.Color : parameters.Colors[0];
        // stdRender preserves authored alpha and ORs the energy byte only for ENERGY_ALPHA.
        return (draw.RawFlags & 2) != 0 ? rgb | ((uint)index << 24) : rgb;
    }
}
