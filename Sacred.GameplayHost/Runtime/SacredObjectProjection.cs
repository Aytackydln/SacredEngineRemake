using System.Numerics;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Runtime;

/// <summary>Gold 0x6239C0/0x623940 object-plane inverse projection in startup
/// PC=24. Renderer matrices are explicit inputs; zero/guessed matrices do not
/// establish a valid world projection.</summary>
public sealed record SacredObjectProjection(Matrix4x4 View, Matrix4x4 Projection)
{
    public Vector3 Project(int nativeX, int nativeY, float height)
    {
        var arithmetic = new SacredResultArithmetic(SacredX87Precision.Single);
        const float horizontal = 0.8944272f, vertical = 0.4472136f;
        var x = arithmetic.Add(arithmetic.Multiply((float)nativeX, horizontal), arithmetic.Multiply((float)nativeY, -horizontal));
        var y = arithmetic.Add(arithmetic.Multiply((float)nativeX, vertical), arithmetic.Multiply((float)nativeY, vertical));
        var determinant = arithmetic.Subtract(arithmetic.Multiply(View.M11, View.M22), arithmetic.Multiply(View.M21, View.M12));
        if (determinant == 0 || Projection.M11 == 0 || Projection.M22 == 0)
            throw new InvalidDataException("Object projection requires initialized nondegenerate native renderer matrices.");
        var inverse = arithmetic.Divide(1, determinant);
        var px = arithmetic.Multiply(arithmetic.Multiply(arithmetic.Divide(2, Projection.M11), x), 1f / 1024);
        var py = arithmetic.Multiply(arithmetic.Multiply(arithmetic.Divide(2, Projection.M22), y), 1f / 768);
        var result = new Vector3(
            (float)arithmetic.Multiply(arithmetic.Subtract(arithmetic.Multiply(View.M22, px), arithmetic.Multiply(View.M21, py)), inverse),
            (float)arithmetic.Multiply(arithmetic.Subtract(arithmetic.Multiply(View.M12, px), arithmetic.Multiply(View.M11, py)), inverse), height);
        if (!float.IsFinite(result.X) || !float.IsFinite(result.Y) || !float.IsFinite(height))
            throw new InvalidDataException("Object projection produced a nonfinite native position.");
        return result;
    }
}
