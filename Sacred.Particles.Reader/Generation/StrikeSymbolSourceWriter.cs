using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class StrikeSymbolSourceWriter
{
    public static string Write(SacredStrikeSymbolFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var textures = string.Join(", ", d.Textures.Select(CSharpLiteral.String));
        var vertices = Convert.ToHexStringLower(MemoryMarshal.AsBytes(d.SurfaceVertices.ToArray().AsSpan()));
        return $", StrikeSymbol = new(Array.AsReadOnly<string>([{textures}]), " +
            $"EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{vertices}\"), {CSharpLiteral.String(d.FlareTexture)}, {CSharpLiteral.String(d.FinishTexture)}) {{ " +
            $"UsesElapsedRate = {d.UsesElapsedRate.ToString().ToLowerInvariant()}, InitialElapsedRate = {F(d.InitialElapsedRate)}, Lifetime = {F(d.Lifetime)}, FrameInterval = {F(d.FrameInterval)}, " +
            $"FlareHalfSize = {F(d.FlareHalfSize)}, FlareOffset = new({F(d.FlareOffset.X)}, {F(d.FlareOffset.Y)}, {F(d.FlareOffset.Z)}), FlareColor = {CSharpLiteral.UInt32(d.FlareColor)}, " +
            $"FlareSourceColorOnly = {d.FlareSourceColorOnly.ToString().ToLowerInvariant()}, StartNotification = {CSharpLiteral.UInt32(d.StartNotification)}, LightStrength = {d.LightStrength}, " +
            $"FinishNotification = {CSharpLiteral.UInt32(d.FinishNotification)}, FinishArgument = {CSharpLiteral.UInt32(d.FinishArgument)}, " +
            $"UsesSavedColor = {d.UsesSavedColor.ToString().ToLowerInvariant()}, InitialColor = {CSharpLiteral.UInt32(d.InitialColor)} }}";
    }
}
