using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class EmperorSourceWriter
{
    public static string Write(SacredEmperorFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var state = d.InitialState;
        var hex = Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1)));
        return $", Emperor = new({CSharpLiteral.String(d.FlareTexture)}) {{ " +
            $"InitialState = System.Runtime.InteropServices.MemoryMarshal.Read<Sacred.Particles.Particles.SacredEmperorFxStateLayout>(Convert.FromHexString(\"{hex}\")), " +
            $"RandomScale = {F(d.RandomScale)}, FlareSizeMinimum = {F(d.FlareSizeMinimum)}, FlareSizeWidth = {F(d.FlareSizeWidth)}, " +
            $"FlareColor = {CSharpLiteral.UInt32(d.FlareColor)}, FlareSourceColorOnly = {d.FlareSourceColorOnly.ToString().ToLowerInvariant()}, " +
            $"RadiusRate = {F(d.RadiusRate)}, InitialRadius = {d.InitialRadius}u, InitialNotification = {d.InitialNotification}u, " +
            $"HitFlag = {d.HitFlag}, HitExtension = {F(d.HitExtension)}, HitFlareMultiplier = {F(d.HitFlareMultiplier)}, " +
            $"HitSizeMultiplier = {F(d.HitSizeMultiplier)}, HitNotification = {d.HitNotification}u, SpecialHitNotification = {d.SpecialHitNotification}u }}";
    }
}
