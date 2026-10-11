using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class RingSurfaceSourceWriter
{
    private static string F(float v) => v.ToString("R",CultureInfo.InvariantCulture)+"f";
    private static string B(bool v) => v.ToString().ToLowerInvariant();
    private static string State<T>(T v) where T:unmanaged =>
        $"System.Runtime.InteropServices.MemoryMarshal.Read<Sacred.Particles.Particles.{typeof(T).Name}>(Convert.FromHexString(\"{Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref v,1)))}\"))";
    private static string Ring(SacredFxRingDefinition d) =>
        $"new(EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{Convert.ToHexStringLower(MemoryMarshal.AsBytes(d.Vertices.ToArray().AsSpan()))}\"), {F(d.FullAngle)}, {d.InitialAngle.ToString("R",CultureInfo.InvariantCulture)}d)";
    public static string Write(SacredBubbleTrapExplosionFxDefinition d) =>
        $", BubbleTrapExplosion = new({CSharpLiteral.String(d.TextureName)}, {Ring(d.Ring)}) {{ InitialState = {State(d.InitialState)}, " +
        $"Lifetime = {F(d.Lifetime)}, RadiusRate = {F(d.RadiusRate)}, RingWidth = {F(d.RingWidth)}, WeightRate = {F(d.WeightRate)}, " +
        $"RedBlueScale = {F(d.RedBlueScale)}, GreenScale = {F(d.GreenScale)}, SegmentCount = {d.SegmentCount}, " +
        $"SourceColorOnly = {B(d.SourceColorOnly)}, Additive = {B(d.Additive)}, InitialNotification = {CSharpLiteral.UInt32(d.InitialNotification)}, InitialLightStrength = {CSharpLiteral.UInt32(d.InitialLightStrength)} }}";
    public static string Write(SacredTerrorSprinterFxDefinition d)
    {
        var l=d.GroundFlare;
        return $", TerrorSprinter = new(new({CSharpLiteral.String(l.TextureName)}, {F(l.HalfSize)}, {CSharpLiteral.UInt32(l.Color)}, default, {F(l.RotationRate)}, {B(l.SourceColorOnly)}, {B(l.Additive)}), " +
            $"Array.AsReadOnly<string>([{string.Join(", ",d.SegmentTextureVariants.Select(CSharpLiteral.String))}]), {Ring(d.Ring)}) {{ InitialState = {State(d.InitialState)}, " +
            $"WeightRate = {F(d.WeightRate)}, RedBlueScale = {F(d.RedBlueScale)}, GreenScale = {F(d.GreenScale)}, RingRadius = {F(d.RingRadius)}, RingHalfWidth = {F(d.RingHalfWidth)}, " +
            $"SegmentCount = {d.SegmentCount}, RingColor = {CSharpLiteral.UInt32(d.RingColor)}, RingSourceColorOnly = {B(d.RingSourceColorOnly)}, RingAdditive = {B(d.RingAdditive)}, " +
            $"InitialNotification = {CSharpLiteral.UInt32(d.InitialNotification)}, PendingCode = {CSharpLiteral.UInt32(d.PendingCode)}, TextureCommand = {CSharpLiteral.UInt32(d.TextureCommand)} }}";
    }
}
