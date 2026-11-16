using System.Numerics;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold event 0x40 / 0x44DDC0 / 0x622660 in startup PC=24.
/// The original fixed isometric basis is initialized before startup; inverse
/// determinant is 1.25. Every projection operation retains native ordering.</summary>
public static class SacredStartupHeading
{
    public static Vector3 ToDirection(float degrees)
    {
        if (!float.IsFinite(degrees) || MathF.Abs(degrees) > 65536)
            throw new NotSupportedException("Heading requires a finite compared angle within 65536 degrees.");
        var arithmetic = new SacredResultArithmetic(SacredX87Precision.Single);
        var radians = arithmetic.Multiply(degrees, 0.017453292f);
        var x = (float)Math.Cos(radians);
        var y = (float)Math.Sin(radians);
        const float horizontal = 0.8944272f, vertical = 0.4472136f;
        return new(
            (float)arithmetic.Multiply(arithmetic.Subtract(arithmetic.Multiply(x, vertical), arithmetic.Multiply(y, -horizontal)), 1.25f),
            (float)arithmetic.Multiply(arithmetic.Subtract(arithmetic.Multiply(y, horizontal), arithmetic.Multiply(x, vertical)), 1.25f), 0);
    }
}
