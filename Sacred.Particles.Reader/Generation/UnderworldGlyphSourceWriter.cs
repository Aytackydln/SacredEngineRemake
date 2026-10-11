using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class UnderworldGlyphSourceWriter
{
    public static string Write(SacredUnderworldGlyphFxDefinition d)
    {
        string Float(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";
        var vertices = Convert.ToHexStringLower(MemoryMarshal.AsBytes(d.RibbonVertices.ToArray().AsSpan()));
        return $", UnderworldGlyph = new({CSharpLiteral.String(d.ModelName)}, {CSharpLiteral.String(d.ModelTexture)}, " +
            $"{CSharpLiteral.String(d.RibbonTexture)}, EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{vertices}\"), {CSharpLiteral.String(d.GlowTexture)}) {{ " +
            $"Element = {CSharpLiteral.UInt32(d.Element)}, FullTurn = {Float(d.FullTurn)}, RandomScale = {Float(d.RandomScale)}, " +
            $"WaveRateOrigin = {Float(d.WaveRateOrigin)}, WaveRateWidth = {Float(d.WaveRateWidth)}, WaveRadiusOrigin = {Float(d.WaveRadiusOrigin)}, WaveRadiusWidth = {Float(d.WaveRadiusWidth)}, " +
            $"PointPhaseStep = {Float(d.PointPhaseStep)}, HalfTurn = {Float(d.HalfTurn)}, WaveRadiusScale = {Float(d.WaveRadiusScale)}, WaveHeightOrigin = {d.WaveHeightOrigin}, " +
            $"ModelScale = {Float(d.ModelScale)}, ModelHeight = {Float(d.ModelHeight)}, ModelColor = {CSharpLiteral.UInt32(d.ModelColor)}, ModelSourceColorOnly = {d.ModelSourceColorOnly.ToString().ToLowerInvariant()}, " +
            $"FirstUScrollRate = {Float(d.FirstUScrollRate)}, FirstVScrollRate = {Float(d.FirstVScrollRate)}, SecondUScrollRate = {Float(d.SecondUScrollRate)}, SecondVScrollRate = {Float(d.SecondVScrollRate)}, " +
            $"RibbonHeight = {Float(d.RibbonHeight)}, RibbonSideY = {Float(d.RibbonSideY)}, RibbonSideZ = {Float(d.RibbonSideZ)}, RibbonNormalMinimum = {Float(d.RibbonNormalMinimum)}, " +
            $"RibbonHalfWidth = {Float(d.RibbonHalfWidth)}, InitialThickness = {Float(d.InitialThickness)}, RibbonSourceColorOnly = {d.RibbonSourceColorOnly.ToString().ToLowerInvariant()}, " +
            $"GlowHalfSize = {Float(d.GlowHalfSize)}, GlowHeight = {Float(d.GlowHeight)}, GlowColor = {CSharpLiteral.UInt32(d.GlowColor)}, GlowSourceColorOnly = {d.GlowSourceColorOnly.ToString().ToLowerInvariant()} }}";
    }
}
