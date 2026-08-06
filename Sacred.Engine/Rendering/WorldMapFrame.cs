using System.Numerics;

namespace Sacred.Engine.Rendering;

public readonly record struct WorldMapFrame(
    ScreenFrame Map,
    Vector2 Center,
    float Zoom,
    WorldMapOverlay Overlay);

public readonly record struct WorldMapOverlay(
    Vector2 TargetWorldPosition,
    Vector2 TargetScreenPosition,
    bool TargetMarkerVisible,
    bool MinimapVisible,
    string DifficultyDisplayName);
