using System.Globalization;

namespace Sacred.Particles.Reader.Generation;

internal static class ColumnFlareSourceWriter
{
    public static string Write(SacredColumnFlareFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var l = d.Layer;
        return $", ColumnFlare = new(new({CSharpLiteral.String(l.TextureName)}, {F(l.HalfSize)}, {CSharpLiteral.UInt32(l.Color)}, " +
            $"new({F(l.Offset.X)}, {F(l.Offset.Y)}, {F(l.Offset.Z)}), {F(l.RotationRate)}, {l.SourceColorOnly.ToString().ToLowerInvariant()}, {l.Additive.ToString().ToLowerInvariant()})) {{ " +
            $"SliceCount = {d.SliceCount}, InitialLifetime = {F(d.InitialLifetime)}, RiseRate = {F(d.RiseRate)}, FallRate = {F(d.FallRate)}, FadeLeadTime = {F(d.FadeLeadTime)}, " +
            $"ColorFrequency = {F(d.ColorFrequency)}, ColorBias = {F(d.ColorBias)}, ColorAmplitude = {F(d.ColorAmplitude)}, ColorOffset = {F(d.ColorOffset)}, ColorScale = {F(d.ColorScale)}, " +
            $"BlueChannel = {CSharpLiteral.UInt32(d.BlueChannel)}, LightBlue = {F(d.LightBlue)}, GroundAlphaScale = {F(d.GroundAlphaScale)}, " +
            $"SliceAlphaScale = {F(d.SliceAlphaScale)}, SliceAlphaFalloff = {F(d.SliceAlphaFalloff)}, HeightSineBias = {F(d.HeightSineBias)}, HeightScale = {F(d.HeightScale)}, " +
            $"InitialNotification = {CSharpLiteral.UInt32(d.InitialNotification)}, InitialPendingCode = {CSharpLiteral.UInt32(d.InitialPendingCode)} }}";
    }
}
