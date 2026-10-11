using System.Globalization;

namespace Sacred.Particles.Reader.Generation;

internal static class LayeredFlareSourceWriter
{
    public static string Write(SacredLayeredFlareFxDefinition definition)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        string B(bool v) => v.ToString().ToLowerInvariant();
        var layers = string.Join(", ", definition.Layers.Select(layer =>
            $"new({CSharpLiteral.String(layer.TextureName)}, {F(layer.HalfSize)}, {CSharpLiteral.UInt32(layer.Color)}, " +
            $"new({F(layer.Offset.X)}, {F(layer.Offset.Y)}, {F(layer.Offset.Z)}), {F(layer.RotationRate)}, {B(layer.SourceColorOnly)}, {B(layer.Additive)})"));
        return $", LayeredFlare = new(Array.AsReadOnly<SacredFxFlareLayer>([{layers}])) {{ " +
            $"InitialLifetime = {F(definition.InitialLifetime)}, InitialAuxiliary = {CSharpLiteral.UInt32(definition.InitialAuxiliary)}, " +
            $"DurationScale = {F(definition.DurationScale)}, StartNotification = {CSharpLiteral.UInt32(definition.StartNotification)}, " +
            $"CanExtendDuration = {B(definition.CanExtendDuration)} }}";
    }
}
