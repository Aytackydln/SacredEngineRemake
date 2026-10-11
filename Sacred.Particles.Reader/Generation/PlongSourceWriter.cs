using System.Globalization;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Reader.Generation;

internal static class PlongSourceWriter
{
    public static string Write(SacredPlongFxDefinition d)
    {
        string F(float v) => v.ToString("R", CultureInfo.InvariantCulture) + "f";
        var state = d.InitialState;
        var hex = Convert.ToHexStringLower(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref state, 1)));
        return ", Plong = new() { " +
            $"InitialState = System.Runtime.InteropServices.MemoryMarshal.Read<Sacred.Particles.Particles.SacredPlongFxStateLayout>(Convert.FromHexString(\"{hex}\")), " +
            $"RandomScale = {F(d.RandomScale)}, SpawnWidth = {F(d.SpawnWidth)}, SpawnOrigin = {F(d.SpawnOrigin)}, " +
            $"SpawnHeight = {F(d.SpawnHeight)}, VelocityAngle = {F(d.VelocityAngle)}, HorizontalSpeed = {F(d.HorizontalSpeed)}, " +
            $"VerticalSpeedWidth = {F(d.VerticalSpeedWidth)}, VerticalSpeedOrigin = {F(d.VerticalSpeedOrigin)}, " +
            $"InitialHalfSize = {F(d.InitialHalfSize)}, FullTurn = {F(d.FullTurn)}, Gravity = {F(d.Gravity)}, " +
            $"BounceFactor = {F(d.BounceFactor)}, ShrinkRate = {F(d.ShrinkRate)} }}";
    }
}
