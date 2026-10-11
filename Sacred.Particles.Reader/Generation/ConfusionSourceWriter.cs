using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class ConfusionSourceWriter
{
    public static string Write(SacredConfusionFxDefinition d)
    {
        string F(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";
        var layers = string.Join(", ", d.GroundLayers.Select(l => $"new({CSharpLiteral.String(l.TextureName)}, {F(l.HalfSize)}, {CSharpLiteral.UInt32(l.Color)}, new({F(l.Offset.X)}, {F(l.Offset.Y)}, {F(l.Offset.Z)}), {F(l.RotationRate)}, {l.SourceColorOnly.ToString().ToLowerInvariant()}, {l.Additive.ToString().ToLowerInvariant()})"));
        var vertices = Convert.ToHexStringLower(MemoryMarshal.AsBytes(d.RibbonVertices.ToArray().AsSpan()));
        var state = Convert.ToHexStringLower(MemoryMarshal.AsBytes(new[] { d.InitialState }.AsSpan()));
        return $", Confusion = new(Array.AsReadOnly<SacredFxFlareLayer>([{layers}]), {CSharpLiteral.String(d.RibbonTexture)}, " +
            $"EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{vertices}\"), EmbeddedParticleParameters.ReadConfusionState(\"{state}\")) {{ " +
            $"PhaseRate = {F(d.PhaseRate)}, PhaseWrap = {F(d.PhaseWrap)}, AlphaAngleScale = {F(d.AlphaAngleScale)}, AlphaScale = {F(d.AlphaScale)}, " +
            $"RandomScale = {F(d.RandomScale)}, RandomAngleScale = {F(d.RandomAngleScale)}, RibbonTimeScale = {F(d.RibbonTimeScale)}, " +
            $"MaximumGroundHeight = {F(d.MaximumGroundHeight)}, ResetGroundHeight = {F(d.ResetGroundHeight)}, StartNotification = {CSharpLiteral.UInt32(d.StartNotification)} }}";
    }
}
