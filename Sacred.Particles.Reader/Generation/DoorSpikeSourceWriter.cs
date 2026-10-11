using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class DoorSpikeSourceWriter
{
    public static string Write(SacredDoorSpikeFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var state = d.InitialState;
        var hex = Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1)));
        return $", DoorSpike = new({CSharpLiteral.String(d.ModelName)}, {CSharpLiteral.String(d.SurfaceTexture)}) {{ " +
            $"InitialState = System.Runtime.InteropServices.MemoryMarshal.Read<Sacred.Particles.Particles.SacredDoorSpikeFxStateLayout>(Convert.FromHexString(\"{hex}\")), " +
            $"DrawCount = {d.DrawCount}, EvenHeading = {F(d.EvenHeading)}, OddHeading = {F(d.OddHeading)}, Tilt = {F(d.Tilt)}, " +
            $"ReleaseFlag = {d.ReleaseFlag}, HasShadowPass = {d.HasShadowPass.ToString().ToLowerInvariant()} }}";
    }
}
