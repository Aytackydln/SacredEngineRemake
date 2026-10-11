using System.Numerics;

namespace Sacred.Particles;

/// <summary>Shared native geometry/clock for Element Stones, independent of any renderer.</summary>
public sealed class SacredElementStoneFxPlayback(SacredElementStoneFxDefinition definition)
{
    public SacredElementStoneFxDefinition Definition { get; } = definition;
    public float ElapsedTime { get; private set; }
    public float Rotation { get; private set; }

    public void Restart() { ElapsedTime = 0; Rotation = 0; }
    public void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        ElapsedTime += seconds;
        var rotation = Rotation + (double)seconds;
        Rotation = (float)(rotation > Definition.FullTurn ? rotation - Definition.FullTurn : rotation);
    }

    /// <summary>Gold's scale * rotationZ, before the caller's camera projection.</summary>
    public Vector3 ModelPosition(Vector3 nativeVertex) => Rotate(nativeVertex * Definition.ModelScale, Rotation)
        + new Vector3(0, 0, Definition.ModelHeight);

    public Vector3 BeamPosition(int pass, int vertex)
    {
        var angle = Rotation + (pass switch { 0 => 0, 1 => Definition.SecondBeamAngle,
            2 => Definition.ThirdBeamAngle, _ => throw new ArgumentOutOfRangeException(nameof(pass)) });
        return Rotate(Definition.BeamVertices[vertex].Position, angle) + new Vector3(0, 0, Definition.BeamHeight);
    }

    /// <summary>One CRT rand call per render, shared by both corner pairs and the core size.</summary>
    public Vector2 FlickerTextureCoordinate(int vertex, int randomValue)
    {
        if ((uint)vertex >= Definition.FlickerVertices.Count) throw new ArgumentOutOfRangeException(nameof(vertex));
        var v = (float)(randomValue * (double)Definition.RandomScale);
        return new(vertex < 2 ? v : (float)(v + (double)Definition.FlickerUWidth),
            vertex % 2 == 0 ? Definition.FlickerEdgeV : 0);
    }

    public float CoreHalfSize(int randomValue) => (float)((float)(randomValue * (double)Definition.RandomScale)
        + (double)Definition.CoreSizeAddend);

    private static Vector3 Rotate(Vector3 vertex, float angle)
    {
        var sine = Math.Sin(angle); var cosine = Math.Cos(angle);
        return new((float)(vertex.X * cosine + vertex.Y * sine),
            (float)(-vertex.X * sine + vertex.Y * cosine), vertex.Z);
    }
}
