using System.Globalization;

namespace Sacred.Particles.Reader.Generation;

internal static class ControlledFlareSourceWriter
{
    public static string Write(SacredControlledFlareFxDefinition definition)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        string B(bool v) => v.ToString().ToLowerInvariant();
        var layers = string.Join(", ", definition.Layers.Select(layer =>
            $"new({CSharpLiteral.String(layer.TextureName)}, {F(layer.HalfSize)}, {CSharpLiteral.UInt32(layer.Color)}, " +
            $"new({F(layer.Offset.X)}, {F(layer.Offset.Y)}, {F(layer.Offset.Z)}), {F(layer.RotationRate)}, {B(layer.SourceColorOnly)}, {B(layer.Additive)})"));
        return $", ControlledFlare = new(SacredControlledFlareKind.{definition.Kind}, Array.AsReadOnly<SacredFxFlareLayer>([{layers}])) {{ " +
            $"RiseRate = {F(definition.RiseRate)}, FallRate = {F(definition.FallRate)}, RotationRate = {F(definition.RotationRate)}, " +
            $"FullAngle = {F(definition.FullAngle)}, AlphaScale = {F(definition.AlphaScale)}, InitialWeight = {F(definition.InitialWeight)}, " +
            $"InitialRadius = {F(definition.InitialRadius)}, RadiusScale = {F(definition.RadiusScale)}, LayerTimeIncrement = {F(definition.LayerTimeIncrement)}, " +
            $"RotationNumerator = {F(definition.RotationNumerator)}, RadiusAmplitude = {F(definition.RadiusAmplitude)}, " +
            $"AtlasSide = {definition.AtlasSide}, InitialAtlasCell = {definition.InitialAtlasCell}, ToggleFlag = {definition.ToggleFlag}, " +
            $"InitialNotification = {CSharpLiteral.UInt32(definition.InitialNotification)}, ToggleNotification = {CSharpLiteral.UInt32(definition.ToggleNotification)} }}";
    }
}
