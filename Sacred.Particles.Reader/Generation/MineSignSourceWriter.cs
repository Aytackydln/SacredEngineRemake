using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class MineSignSourceWriter
{
    public static string Write(SacredMineSignFxDefinition d)
    {
        string F(float value) => value.ToString("R", CultureInfo.InvariantCulture) + "f";
        var state = d.InitialState;
        var hex = Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1)));
        return $", MineSign = new() {{ InitialState = System.Runtime.InteropServices.MemoryMarshal.Read<Sacred.Particles.Particles.SacredMineSignFxStateLayout>(Convert.FromHexString(\"{hex}\")), " +
            $"BirthRate = {F(d.BirthRate)}, MaximumCarry = {F(d.MaximumCarry)}, BirthUnit = {F(d.BirthUnit)}, " +
            $"RingRadius = {F(d.RingRadius)}, FullAngle = {F(d.FullAngle)}, RandomScale = {F(d.RandomScale)}, " +
            $"RotationWidth = {F(d.RotationWidth)}, RotationHalfWidth = {F(d.RotationHalfWidth)}, RotationBase = {F(d.RotationBase)}, " +
            $"AudioPitchMinimum = {F(d.AudioPitchMinimum)}, MineUsedFlag = {d.MineUsedFlag} }}";
    }
}
