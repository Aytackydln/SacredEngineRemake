using System;
using System.Numerics;

namespace Sacred.Engine.Scene.WorldMap;

internal sealed class WorldMapCamera
{
    public const float DefaultZoom = 1.0f;
    public const float MaximumZoom = 3.0f;

    public Vector2 Center { get; private set; }
    public float Zoom { get; private set; } = DefaultZoom;

    public void CenterOn(Vector2 mapPosition, int mapWidth, int mapHeight, int viewportWidth, int viewportHeight)
    {
        Center = mapPosition;
        Zoom = Math.Clamp(DefaultZoom, MinimumZoom(mapWidth, mapHeight, viewportWidth, viewportHeight), MaximumZoom);
        Clamp(mapWidth, mapHeight, viewportWidth, viewportHeight);
    }

    public bool Pan(
        Vector2 mapDelta,
        int mapWidth,
        int mapHeight,
        int viewportWidth,
        int viewportHeight)
    {
        var previous = Center;
        Center += mapDelta;
        Clamp(mapWidth, mapHeight, viewportWidth, viewportHeight);
        return Center != previous;
    }

    public bool ChangeZoom(
        float factor,
        Vector2 screenAnchor,
        int mapWidth,
        int mapHeight,
        int viewportWidth,
        int viewportHeight)
    {
        if (!float.IsFinite(factor) || factor <= 0.0f)
            return false;

        var previousZoom = Zoom;
        var mapAnchor = ScreenToMap(screenAnchor, viewportWidth, viewportHeight);
        Zoom = Math.Clamp(
            Zoom * factor,
            MinimumZoom(mapWidth, mapHeight, viewportWidth, viewportHeight),
            MaximumZoom);
        if (Math.Abs(Zoom - previousZoom) <= float.Epsilon)
            return false;

        var screenCenter = new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f);
        Center = mapAnchor - (screenAnchor - screenCenter) / Zoom;
        Clamp(mapWidth, mapHeight, viewportWidth, viewportHeight);
        return true;
    }

    public Vector2 ScreenToMap(Vector2 screenPosition, int viewportWidth, int viewportHeight) =>
        Center + (screenPosition - new Vector2(viewportWidth * 0.5f, viewportHeight * 0.5f)) / Zoom;

    private void Clamp(int mapWidth, int mapHeight, int viewportWidth, int viewportHeight)
    {
        Center = new Vector2(
            ClampAxis(Center.X, mapWidth, viewportWidth / Zoom),
            ClampAxis(Center.Y, mapHeight, viewportHeight / Zoom));
    }

    private static float ClampAxis(float center, int mapSize, float visibleSize)
    {
        if (visibleSize >= mapSize)
            return mapSize * 0.5f;

        var halfVisible = visibleSize * 0.5f;
        return Math.Clamp(center, halfVisible, mapSize - halfVisible);
    }

    private static float MinimumZoom(int mapWidth, int mapHeight, int viewportWidth, int viewportHeight) =>
        Math.Max(0.25f, Math.Min(viewportWidth / (float)mapWidth, viewportHeight / (float)mapHeight));
}
