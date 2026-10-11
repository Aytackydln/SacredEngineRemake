using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class ExpandingBurstSourceWriter
{
    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
    public static string Write(SacredExpandingBurstFxDefinition d)
    {
        var state = d.InitialState;
        var stateHex = Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1)));
        var vertices = Convert.ToHexStringLower(MemoryMarshal.AsBytes(d.Ring.Vertices.ToArray().AsSpan()));
        return $", ExpandingBurst = new({CSharpLiteral.String(d.RingTexture)}, {CSharpLiteral.String(d.GroundTexture)}, " +
            $"new(EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{vertices}\"), {F(d.Ring.FullAngle)}, {d.Ring.InitialAngle.ToString("R", CultureInfo.InvariantCulture)}d)) {{ " +
            $"InitialState = System.Runtime.InteropServices.MemoryMarshal.Read<Sacred.Particles.Particles.SacredExpandingBurstFxStateLayout>(Convert.FromHexString(\"{stateHex}\")), " +
            $"Lifetime = {F(d.Lifetime)}, RingDuration = {F(d.RingDuration)}, RingFadeStart = {F(d.RingFadeStart)}, RingFadeRate = {F(d.RingFadeRate)}, " +
            $"RadiusRate = {F(d.RadiusRate)}, EventRadiusRate = {(d.EventRadiusRate is { } rate ? rate + "u" : "null")}, RingWidth = {F(d.RingWidth)}, " +
            $"RingColorScale = new({F(d.RingColorScale.X)}, {F(d.RingColorScale.Y)}, {F(d.RingColorScale.Z)}), SegmentCount = {d.SegmentCount}, " +
            $"GroundTimeRate = {F(d.GroundTimeRate)}, GroundSizes = new({F(d.GroundSizes.X)}, {F(d.GroundSizes.Y)}), GroundRotations = new({F(d.GroundRotations.X)}, {F(d.GroundRotations.Y)}), " +
            $"GroundColor = {CSharpLiteral.UInt32(d.GroundColor)}, RenderRadiusDecay = {d.RenderRadiusDecay.ToString("R", CultureInfo.InvariantCulture)}d, " +
            $"InitialNotification = {CSharpLiteral.UInt32(d.InitialNotification)}, InitialLightStrength = {CSharpLiteral.UInt32(d.InitialLightStrength)} }}";
    }
}
