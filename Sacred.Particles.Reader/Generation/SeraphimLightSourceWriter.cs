using System.Globalization;
using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles.Reader.Generation;

internal static class SeraphimLightSourceWriter
{
    private static string B(bool value) => value.ToString().ToLowerInvariant();
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";
    private static string Vertices(IReadOnlyList<SacredFxSurfaceVertexLayout> vertices) =>
        $"EmbeddedParticleParameters.ReadFxSurfaceVertices(\"{Convert.ToHexStringLower(MemoryMarshal.AsBytes(vertices.ToArray().AsSpan()))}\")";
    public static string Write(SacredRotatingBladesFxDefinition d) =>
        $", RotatingBlades = new({Vertices(d.TrailVertices)}, {CSharpLiteral.String(d.GroundTexture)}, {CSharpLiteral.String(d.TrailTexture)}, {CSharpLiteral.String(d.TipTexture)}, " +
        string.Join(", ", new[] { d.RotationRate, d.RotationWrap, d.RiseRate, d.MaximumHeight, d.RingAngleStep, d.SampleStep, d.HelixAngleScale, d.TipAngle, d.GroundMargin, d.TipSizeBase, d.TipSizeRandomScale }.Select(F)) +
        $") {{ GroundAdditive = {B(d.GroundAdditive)}, GroundSourceColorOnly = {B(d.GroundSourceColorOnly)}, TrailSourceColorOnly = {B(d.TrailSourceColorOnly)}, DefaultActorRadius = {F(d.DefaultActorRadius)}, RingSamples = {d.RingSamples}, HelixSamples = {d.HelixSamples}, GroundColor = {CSharpLiteral.UInt32(d.GroundColor)} }}";
    public static string Write(SacredCelestialLightFxDefinition d) =>
        $", CelestialLight = new({Vertices(d.ColumnVertices)}, {Vertices(d.EdgeVertices)}, {CSharpLiteral.String(d.GlowTexture)}, {CSharpLiteral.String(d.GroundTexture)}, " +
        string.Join(", ", new[] { d.WaveRate, d.WaveScale, d.WaveOrigin, d.RetirementFactor, d.ColumnAlpha, d.EdgeAlpha, d.GroundHalfSize, d.GlowHalfSize, d.GroundRotationRate }.Select(F)) +
        $", {CSharpLiteral.UInt32(d.GroundRgb)}, {CSharpLiteral.UInt32(d.GlowRgb)}, {F(d.InitialLifetime)}) {{ FlareAdditive = {B(d.FlareAdditive)}, FlareSourceColorOnly = {B(d.FlareSourceColorOnly)} }}";
}
