using System.Numerics;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;
using Sacred.World.Geometry;
using Sacred.World.Networking;

namespace Sacred.GameplayHost.World;

/// <summary>Original physical placement, shared by Engine and terminal observers.</summary>
public static class GameplayObjectPlacement
{
    public static GameplayWorldObject FromReplica(GoldReplicatedObject obj, ItemsPakEntry item, GoldWorldReplica world)
    {
        var state = obj.BaseState;
        var logical = SacredNetworkPosition.ToTiles(state);
        var visual = logical;
        if (obj.Creature is null && obj.Physical is { UsesVectorCC: true } physical)
        {
            // Gold 0x450254 serializes +0xCC separately from the logical cell centre.
            // 0x44B324 consumes this exact root anchor. Preserve its offset through relocation.
            var origin = physical.BaseState;
            visual += new Vector2(physical.SelectedVector.X - origin.X, physical.SelectedVector.Y - origin.Y)
                / SacredNetworkPosition.NativeUnitsPerTile;
        }
        var x = (int)MathF.Floor(logical.X); var y = (int)MathF.Floor(logical.Y);
        var placement = new StaticWorldObject(state.ObjectReference, state.ObjectType, 0, state.Sector,
            0, 0, 0, 0, state.Surface, 0, 0, 0, 0, 0, x + y, y, x, 0, 0)
            {
                PreciseWorldPosition = visual, ScriptSurfaceLevel = state.Surface,
                DoorTriggerId = item.ModelDesc.Category == SacredItemCategory.Door ? state.TriggerReference : null
            };
        var facing = BitConverter.UInt32BitsToSingle(obj.FacingBits ?? state.Word28);
        return new(placement, item, new(visual, 0), WorldModelPose.RotationRadians(facing),
            SacredNetworkObjectAnimation.IsActivated(obj, world), obj.Creature is null && obj.Physical is not null && IsModelProp(item), obj.InstanceVersion)
            { Replica = obj };
    }

    public static GameplayWorldObject FromAuthored(StaticWorldObject placement, ItemsPakEntry item, bool activated) =>
        new(placement, item, new(WorldModelPose.TilePosition(placement), 0),
            WorldModelPose.RotationRadians(item.ModelDesc.Angle3D, placement.ScriptFacingDegrees), activated, IsModelProp(item));

    public static bool IsModelProp(ItemsPakEntry item) =>
        item.ModelDesc.GraphicType.HasFlag(SacredItemGraphicType.Model) && !string.IsNullOrWhiteSpace(item.ModelName) &&
        item.ModelDesc.Category is SacredItemCategory.WorldObject or SacredItemCategory.Container or SacredItemCategory.Door or SacredItemCategory.Effect;
}
