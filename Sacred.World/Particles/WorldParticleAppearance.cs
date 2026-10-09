using Sacred.Particles;

namespace Sacred.World.Particles;

internal static class WorldParticleAppearance
{
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
