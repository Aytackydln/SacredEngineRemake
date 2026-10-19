using System.Numerics;

namespace Sacred.GameplayHost.World;

/// <summary>Gameplay's current drop pose. Height is in native model units; XY is in terrain tiles.</summary>
public readonly record struct GameplayDroppedItemPose(Vector2 Position, float Height, float GroundHeight,
    Matrix4x4 Orientation, bool IsFlying)
{
    public bool IsPickable => !IsFlying;
}
