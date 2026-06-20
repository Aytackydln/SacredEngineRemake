using System.Numerics;

namespace Sacred.Granny;

public readonly record struct GrnCharacterAttachment(
    byte[] Bytes,
    string? RigidAttachBoneName = null,
    string? SourceAttachBoneName = null,
    Vector3? ModelScale = null)
{
    public Vector3 Scale => ModelScale ?? Vector3.One;
}
