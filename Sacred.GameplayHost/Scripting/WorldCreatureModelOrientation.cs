using System.Numerics;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 44DBF0: project the two absolute XY points in PC=24,
/// normalize their difference, then submit the right/forward/up basis.
/// Model translation and world placement are separate.</summary>
public static class WorldCreatureModelOrientation
{
    public static Matrix4x4 Create(GoldObjectBaseState actor, GoldPhysicalVector direction,
        SacredObjectProjection projection)
    {
        var arithmetic = new SacredResultArithmetic(SacredX87Precision.Single);
        var start = projection.Project(actor.X, actor.Y, 0);
        var end = projection.Project((float)arithmetic.Add(actor.X, direction.X),
            (float)arithmetic.Add(actor.Y, direction.Y), 0);
        var x = arithmetic.Subtract(end.X, start.X);
        var y = arithmetic.Subtract(end.Y, start.Y);
        var z = arithmetic.Subtract(end.Z, start.Z);
        var lengthSquared = arithmetic.Add(arithmetic.Add(arithmetic.Multiply(x, x), arithmetic.Multiply(y, y)), arithmetic.Multiply(z, z));
        var length = (float)Math.Sqrt(lengthSquared);
        if (length < .0000001) x = y = z = 0; // Native double at 890800.
        else
        {
            var reciprocal = arithmetic.Divide(1, length);
            x = arithmetic.Multiply(x, reciprocal);
            y = arithmetic.Multiply(y, reciprocal);
            z = arithmetic.Multiply(z, reciprocal);
        }
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            throw new InvalidDataException("Native model orientation requires a finite initialized projection.");
        return new((float)x, (float)y, 0, 0, -(float)y, (float)x, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    }
}
