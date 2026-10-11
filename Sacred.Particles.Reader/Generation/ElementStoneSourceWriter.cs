using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class ElementStoneSourceWriter
{
    public static string Write(SacredElementStoneFxDefinition d)
    {
        string String(string value) => CSharpLiteral.String(value);
        string Float(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";
        string Vertices<T>(IReadOnlyList<T> vertices) where T : unmanaged =>
            $"EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{Convert.ToHexStringLower(MemoryMarshal.AsBytes(vertices.ToArray().AsSpan()))}\")";
        return $", ElementStone = new({CSharpLiteral.UInt32(d.ItemModelTypeId)}, {String(d.ModelTexture)}, " +
            $"{String(d.BeamTexture)}, {String(d.FlickerTexture)}, {Vertices(d.BeamVertices)}, {Vertices(d.FlickerVertices)}, " +
            $"{String(d.GlowTexture)}, {String(d.CoreTexture)}) {{ " +
            $"FullTurn = {Float(d.FullTurn)}, ModelScale = {Float(d.ModelScale)}, ModelHeight = {Float(d.ModelHeight)}, " +
            $"BeamHeight = {Float(d.BeamHeight)}, SecondBeamAngle = {Float(d.SecondBeamAngle)}, ThirdBeamAngle = {Float(d.ThirdBeamAngle)}, " +
            $"RandomScale = {Float(d.RandomScale)}, FlickerUWidth = {Float(d.FlickerUWidth)}, FlickerEdgeV = {Float(d.FlickerEdgeV)}, " +
            $"GlowHalfSize = {Float(d.GlowHalfSize)}, GlowHeight = {Float(d.GlowHeight)}, GlowColor = {CSharpLiteral.UInt32(d.GlowColor)}, " +
            $"CoreColor = {CSharpLiteral.UInt32(d.CoreColor)}, FlickerBottomColor = {CSharpLiteral.UInt32(d.FlickerBottomColor)}, CoreSizeAddend = {Float(d.CoreSizeAddend)}, CoreHeight = {Float(d.CoreHeight)}, " +
            $"GlowSourceColorOnly = {d.GlowSourceColorOnly.ToString().ToLowerInvariant()}, CoreSourceColorOnly = {d.CoreSourceColorOnly.ToString().ToLowerInvariant()}, " +
            $"ModelSourceColorOnly = {d.ModelSourceColorOnly.ToString().ToLowerInvariant()}, SurfaceSourceColorOnly = {d.SurfaceSourceColorOnly.ToString().ToLowerInvariant()} }}";
    }
}
