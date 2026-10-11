using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class PongBatterSourceWriter
{
    public static string Write(SacredPongBatterFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        string V(Vector3 v) => $"new({F(v.X)}, {F(v.Y)}, {F(v.Z)})";
        var vertices = Convert.ToHexStringLower(MemoryMarshal.AsBytes(d.SurfaceVertices.ToArray().AsSpan()));
        var edges = string.Join(", ", d.Edges.Select(e => $"new({V(e.Start)}, {V(e.End)})"));
        return $", PongBatter = new({CSharpLiteral.String(d.SurfaceTexture)}, {CSharpLiteral.String(d.EdgeTexture)}, " +
            $"EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{vertices}\"), Array.AsReadOnly<SacredFxBeamSegment>([{edges}])) {{ " +
            $"ComputerControlled = {d.ComputerControlled.ToString().ToLowerInvariant()}, TextureScale = {F(d.TextureScale)}, UScrollRate = {F(d.UScrollRate)}, VScrollRate = {F(d.VScrollRate)}, " +
            $"EdgeSizeOrigin = {F(d.EdgeSizeOrigin)}, EdgeSizeWave = {F(d.EdgeSizeWave)}, EdgeSizeHit = {F(d.EdgeSizeHit)}, EdgeDensityOrigin = {F(d.EdgeDensityOrigin)}, EdgeDensityHit = {F(d.EdgeDensityHit)}, " +
            $"SurfaceColorOrigin = {F(d.SurfaceColorOrigin)}, SurfaceColorWave = {F(d.SurfaceColorWave)}, HitFlashReset = {F(d.HitFlashReset)}, EdgeColor = {CSharpLiteral.UInt32(d.EdgeColor)} }}";
    }
}
