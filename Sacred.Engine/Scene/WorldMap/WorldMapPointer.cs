using System;
using System.Numerics;

namespace Sacred.Engine.Scene.WorldMap;

internal sealed class WorldMapPointer
{
    public Vector2 Position { get; private set; }

    public void Place(Vector2 mapPosition, int mapWidth, int mapHeight) =>
        Position = Vector2.Clamp(mapPosition, Vector2.Zero, new Vector2(mapWidth - 1, mapHeight - 1));

    public bool Move(Vector2 direction, float deltaSeconds, float zoom, int mapWidth, int mapHeight)
    {
        if (direction.LengthSquared() > 1.0f)
            direction = Vector2.Normalize(direction);
        var previous = Position;
        Place(Position + direction * 650.0f * Math.Max(0.0f, deltaSeconds) / zoom, mapWidth, mapHeight);
        return Position != previous;
    }
}
