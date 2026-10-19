using System.Numerics;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>One gameplay instance. Logical cell/depth and exact visual pivot remain distinct.</summary>
public sealed record GameplayWorldObject(StaticWorldObject Placement, ItemsPakEntry Definition,
    Vector3 Position, float RotationRadians, bool IsActivated, bool IsModelProp, long InstanceVersion = 0)
{
    public uint Reference => Placement.StaticId;
    public GoldReplicatedObject? Replica { get; init; }
    public GameplayDroppedItemPose? Drop { get; init; }
    public bool CanInteract => Definition.ModelDesc.IsSelectable && (Drop is null || Drop.Value.IsPickable);
}
