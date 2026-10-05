using System.Numerics;
using Sacred.Core.World.Stairs;

namespace Sacred.Engine.Scene.InGame;

internal readonly record struct WorldFocusTarget(uint Id, Vector2 Position, SceneModel? Model,
    WorldStairsLink? Stairs = null)
{
    public string Kind => Stairs is null ? "object" : "stairs";
}
