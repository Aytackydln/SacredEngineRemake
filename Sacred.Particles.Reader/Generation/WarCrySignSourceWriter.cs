using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class WarCrySignSourceWriter
{
    public static string Write(SacredWarCrySignFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var vertices = Convert.ToHexStringLower(MemoryMarshal.AsBytes(d.StripVertices.ToArray().AsSpan()));
        var l = d.GroundFlare;
        return $", WarCrySign = new({CSharpLiteral.String(d.StripTexture)}, EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{vertices}\"), " +
            $"new({CSharpLiteral.String(l.TextureName)}, {F(l.HalfSize)}, {CSharpLiteral.UInt32(l.Color)}, default, {F(l.RotationRate)}, {l.SourceColorOnly.ToString().ToLowerInvariant()}, {l.Additive.ToString().ToLowerInvariant()})) {{ " +
            $"InitialLifetime = {F(d.InitialLifetime)}, MinimumReach = {F(d.MinimumReach)}, MaximumReach = {F(d.MaximumReach)}, " +
            $"WeightRate = {F(d.WeightRate)}, SweepRate = {F(d.SweepRate)}, SweepPeriod = {F(d.SweepPeriod)}, " +
            $"SectorCount = {d.SectorCount}, SectorStep = {F(d.SectorStep)}, SweepWidth = {F(d.SweepWidth)}, ReachFalloff = {F(d.ReachFalloff)}, " +
            $"FullAngle = {F(d.FullAngle)}, OppositeAngle = {F(d.OppositeAngle)}, StripColor = {CSharpLiteral.UInt32(d.StripColor)}, " +
            $"HighlightThreshold = {F(d.HighlightThreshold)}, HighlightAlphaScale = {F(d.HighlightAlphaScale)}, HighlightPassCount = {d.HighlightPassCount}, InitialNotification = {CSharpLiteral.UInt32(d.InitialNotification)} }}";
    }
}
