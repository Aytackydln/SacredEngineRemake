using System.Globalization;

namespace Sacred.Particles.Reader.Generation;

internal static class StonedSourceWriter
{
    public static string Write(SacredStonedFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        return $", Stoned = new({d.ItemModelTypeId}u, {CSharpLiteral.String(d.OwnerTexture)}, {CSharpLiteral.String(d.AuraTexture)}) {{ " +
            $"DefaultDuration = {F(d.DefaultDuration)}, RandomScale = {F(d.RandomScale)}, FullTurn = {F(d.FullTurn)}, " +
            $"StartNotification = {d.StartNotification}u, OwnerTextureMask = {d.OwnerTextureMask}u, OwnerTextureOperation = {d.OwnerTextureOperation}u }}";
    }
}
