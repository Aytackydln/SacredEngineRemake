using System.Globalization;

namespace Sacred.Particles.Reader.Generation;

internal static class SpriteFlareSourceWriter
{
    public static string Write(SacredSpriteFlareFxDefinition definition)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        string B(bool v) => v.ToString().ToLowerInvariant();
        string Layer(SacredFxFlareLayer layer) =>
            $"new({CSharpLiteral.String(layer.TextureName)}, {F(layer.HalfSize)}, {CSharpLiteral.UInt32(layer.Color)}, " +
            $"new({F(layer.Offset.X)}, {F(layer.Offset.Y)}, {F(layer.Offset.Z)}), {F(layer.RotationRate)}, {B(layer.SourceColorOnly)}, {B(layer.Additive)})";
        return $", SpriteFlare = new(Array.AsReadOnly<SacredFxFlareLayer>([{string.Join(", ", definition.Layers.Select(Layer))}])) {{ " +
            $"GroundFlare = {(definition.GroundFlare is { } ground ? Layer(ground) : "null")}, OscillatesColors = {B(definition.OscillatesColors)}, " +
            $"Lifetime = {F(definition.Lifetime)}, ColorFrequency = {F(definition.ColorFrequency)}, ColorAmplitude = {F(definition.ColorAmplitude)}, " +
            $"ColorBase = {CSharpLiteral.UInt32(definition.ColorBase)}, ComplementIntensity = {CSharpLiteral.UInt32(definition.ComplementIntensity)}, " +
            $"PulseThreshold = {F(definition.PulseThreshold)}, PulseRiseScale = {F(definition.PulseRiseScale)}, PulseFallScale = {F(definition.PulseFallScale)}, " +
            $"PulseAmplitude = {F(definition.PulseAmplitude)}, RemovalFlag = {definition.RemovalFlag}, " +
            $"InitialNotification = {CSharpLiteral.UInt32(definition.InitialNotification)}, InitialLightStrength = {CSharpLiteral.UInt32(definition.InitialLightStrength)}, " +
            $"InitialAuxiliary = {CSharpLiteral.UInt32(definition.InitialAuxiliary)} }}";
    }
}
