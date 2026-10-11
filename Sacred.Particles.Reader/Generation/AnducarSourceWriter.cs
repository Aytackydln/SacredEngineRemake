using System.Globalization;

namespace Sacred.Particles.Reader.Generation;

internal static class AnducarSourceWriter
{
    public static string Write(SacredAnducarFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var models = string.Join(", ", d.ModelNames.Select(CSharpLiteral.String));
        return $", Anducar = new(Array.AsReadOnly<string>([{models}]), {CSharpLiteral.String(d.SurfaceTexture)}, {CSharpLiteral.String(d.GlowTexture)}) {{ " +
            $"Count = {d.Count}, HasGlowPass = {d.HasGlowPass.ToString().ToLowerInvariant()}, RandomScale = {F(d.RandomScale)}, ScaleOrigin = {F(d.ScaleOrigin)}, ScaleWidth = {F(d.ScaleWidth)}, " +
            $"FullTurn = {F(d.FullTurn)}, OffsetWidth = {F(d.OffsetWidth)}, OffsetOrigin = {F(d.OffsetOrigin)}, DelayPerUnit = {F(d.DelayPerUnit)}, HoldSeconds = {F(d.HoldSeconds)}, " +
            $"EnvelopeRate = {F(d.EnvelopeRate)}, HorizontalScaleFactor = {F(d.HorizontalScaleFactor)}, GlowColor = {CSharpLiteral.UInt32(d.GlowColor)}, " +
            $"GlowSourceColorOnly = {d.GlowSourceColorOnly.ToString().ToLowerInvariant()}, StartNotification = {CSharpLiteral.UInt32(d.StartNotification)} }}";
    }
}
