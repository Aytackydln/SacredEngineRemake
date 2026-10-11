using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class ForgeElementSourceWriter
{
    public static string Write(SacredForgeElementFxDefinition d)
    {
        string Float(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";
        var state = d.InitialState;
        var vertices = Convert.ToHexStringLower(MemoryMarshal.AsBytes(d.RingVertices.ToArray().AsSpan()));
        var initial = Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1)));
        return $", ForgeElement = new({CSharpLiteral.String(d.ModelName)}, {CSharpLiteral.String(d.ModelTexture)}, " +
            $"{CSharpLiteral.String(d.RingTexture)}, {CSharpLiteral.String(d.RotatingFlareTexture)}, {CSharpLiteral.String(d.GroundFlareTexture)}, " +
            $"EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{vertices}\"), EmbeddedParticleParameters.ReadForgeState(\"{initial}\")) {{ " +
            $"GrowthRate = {Float(d.GrowthRate)}, MaximumGrowthFactor = {Float(d.MaximumGrowthFactor)}, CompressionRate = {Float(d.CompressionRate)}, MinimumCompressionFactor = {Float(d.MinimumCompressionFactor)}, " +
            $"FadeRate = {Float(d.FadeRate)}, ScrollRate = {Float(d.ScrollRate)}, ScrollWrap = {Float(d.ScrollWrap)}, " +
            $"FlareHalfSize = {Float(d.FlareHalfSize)}, FlareHeight = {Float(d.FlareHeight)}, FullTurn = {Float(d.FullTurn)}, HalfTurn = {Float(d.HalfTurn)}, FlareAngleBias = {Float(d.FlareAngleBias)}, " +
            $"ModelBobRate = {Float(d.ModelBobRate)}, ModelBobAmplitude = {Float(d.ModelBobAmplitude)}, ModelHeight = {Float(d.ModelHeight)}, RingHeight = {Float(d.RingHeight)}, " +
            $"TrailStartBrightness = {d.TrailStartBrightness}, TrailBrightnessStep = {d.TrailBrightnessStep}, " +
            $"ChildDelay = {Float(d.ChildDelay)}, ChildFrameRate = {Float(d.ChildFrameRate)}, ChildLimit = {Float(d.ChildLimit)}, ChildTypeId = {CSharpLiteral.UInt32(d.ChildTypeId)}, ModelSourceColorOnly = {d.ModelSourceColorOnly.ToString().ToLowerInvariant()} }}";
    }
}
