using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class BloodFountainSourceWriter
{
    public static string Write(SacredBloodFountainFxDefinition d)
    {
        string F(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";
        var state = d.InitialState;
        var hex = Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1)));
        return $", BloodFountain = new() {{ InitialState = System.Runtime.InteropServices.MemoryMarshal.Read<Sacred.Particles.Particles.SacredBloodFountainFxStateLayout>(Convert.FromHexString(\"{hex}\")), " +
            $"FadeStart = {F(d.FadeStart)}, FadeRate = {F(d.FadeRate)}, JetSpeed = {F(d.JetSpeed)} }}";
    }
}
