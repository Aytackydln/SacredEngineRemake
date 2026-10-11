using System.Globalization;

namespace Sacred.Particles.Reader.Generation;

internal static class PulseFlareSourceWriter
{
    public static string Write(SacredPulseFlareFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var layers = string.Join(", ", d.Layers.Select(l => $"new({CSharpLiteral.String(l.TextureName)}, {F(l.HalfSize)}, {CSharpLiteral.UInt32(l.Color)}, " +
            $"new({F(l.Offset.X)}, {F(l.Offset.Y)}, {F(l.Offset.Z)}), {F(l.RotationRate)}, {l.SourceColorOnly.ToString().ToLowerInvariant()}, {l.Additive.ToString().ToLowerInvariant()})"));
        return $", PulseFlare = new(SacredPulseFlareKind.{d.Kind}, new SacredFxFlareLayer[] {{ {layers} }}) {{ " +
            $"Lifetime = {F(d.Lifetime)}, ResetInterval = {F(d.ResetInterval)}, RandomScale = {F(d.RandomScale)}, PositionRange = {F(d.PositionRange)}, " +
            $"PositionBias = {F(d.PositionBias)}, FullAngle = {F(d.FullAngle)}, PlaneCount = {d.PlaneCount}, PlaneStep = {F(d.PlaneStep)}, " +
            $"SpreadScale = {F(d.SpreadScale)}, AlphaBias = {d.AlphaBias.ToString("R", CultureInfo.InvariantCulture)}, AlphaScale = {d.AlphaScale.ToString("R", CultureInfo.InvariantCulture)}, FlashAlpha = {CSharpLiteral.UInt32(d.FlashAlpha)}, " +
            $"FadeRate = {F(d.FadeRate)}, InitialTimer = {F(d.InitialTimer)}, ResetTimer = {F(d.ResetTimer)}, HitTimer = {F(d.HitTimer)}, " +
            $"InitialSpeed = {CSharpLiteral.UInt32(d.InitialSpeed)}, SpeedIncrement = {CSharpLiteral.UInt32(d.SpeedIncrement)}, SpeedLimit = {CSharpLiteral.UInt32(d.SpeedLimit)}, ResumeNotification = {CSharpLiteral.UInt32(d.ResumeNotification)}, " +
            $"InitialNotification = {CSharpLiteral.UInt32(d.InitialNotification)}, HitFlag = {d.HitFlag}, RadiusFrequency = {F(d.RadiusFrequency)}, RadiusAmplitude = {F(d.RadiusAmplitude)}, " +
            $"RadiusBias = {F(d.RadiusBias)} }}";
    }
}
