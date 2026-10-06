using System.Numerics;
using Sacred.Core.World.Sector;
using Sacred.Core.World.Stairs;

namespace Sacred.Engine.Scene.InGame;

internal readonly record struct WorldFocusTarget(uint Id, Vector2 Position, SceneModel? Model,
    WorldStairsLink? Stairs = null)
{
    public IndoorStairTarget? IndoorStair { get; init; }
    public bool IsStairs => Stairs is not null || IndoorStair is not null;
    public string Kind => IsStairs ? "stairs" : "object";
}

internal readonly record struct IndoorStairTarget(IndoorTileGroupId Group, IndoorTriggerTile Trigger);
