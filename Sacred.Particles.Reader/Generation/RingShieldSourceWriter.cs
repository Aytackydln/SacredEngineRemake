using System.Globalization;
using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles.Reader.Generation;

internal static class RingShieldSourceWriter
{
    public static string Write(SacredRingShieldFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        string Vertices(IReadOnlyList<SacredFxSurfaceVertexLayout> vertices) =>
            $"EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{Convert.ToHexStringLower(MemoryMarshal.AsBytes(vertices.ToArray().AsSpan()))}\")";
        var holy = d.Holy is not { } h ? "null" : $"new({F(h.RotationRate)}, {F(h.RotationWrap)}, {h.HitBrightness}, {F(h.HitFadeRate)}, {F(h.GradientIndexScale)}, {h.GradientStart}, {h.GradientStep}, {F(h.GroundHalfSize)}, {F(h.GroundAngleScale)}, {F(h.GroundAngleOrigin)}, {F(h.GroundAngleBias)}, {h.GroundSourceColorOnly.ToString().ToLowerInvariant()})";
        var magic = d.Magic is not { } m ? "null" : $"new({F(m.GrowthRate)}, {F(m.MaximumSegments)}, {F(m.HitFlash)}, {F(m.HitFadeRate)}, {F(m.MarkerScaleOrigin)}, {F(m.MarkerScaleWave)}, {F(m.HeightScaleOrigin)}, {F(m.HeightScaleWave)}, {F(m.FirstHeightWaveRate)}, {F(m.SecondHeightWaveRate)})";
        return $", RingShield = new({Vertices(d.RingVertices)}, {CSharpLiteral.String(d.SurfaceTexture)}, {CSharpLiteral.String(d.SecondaryTexture)}) {{ " +
            $"Holy = {holy}, Magic = {magic}, MarkerTexture = {CSharpLiteral.String(d.MarkerTexture)}, MarkerVertices = {Vertices(d.MarkerVertices)}, " +
            $"AngleStep = {F(d.AngleStep)}, Radius = {F(d.Radius)}, InitialLifetime = {F(d.InitialLifetime)}, StartNotification = {CSharpLiteral.UInt32(d.StartNotification)}, LightStrength = {d.LightStrength} }}";
    }
}
