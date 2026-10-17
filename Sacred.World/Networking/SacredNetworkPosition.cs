using System.Numerics;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

/// <summary>Network object coordinates use the same native world units as authored KEYX bounds and world-position scripts.</summary>
public static class SacredNetworkPosition
{
    // Original Gold world-coordinate scale at 0x890DC0, also used by WorldObjectScriptIndex.
    public const float NativeUnitsPerTile = 53.66563034057617f;
    public static Vector2 ToTiles(GoldObjectBaseState state) => new(state.X / NativeUnitsPerTile, state.Y / NativeUnitsPerTile);
}
