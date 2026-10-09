using System.Numerics;
using Sacred.UI.Maps;
using Sacred.World.Map;

namespace Sacred.Engine.Rendering;

public readonly record struct WorldMapFrame(
    ScreenFrame Map,
    Vector2 Center,
    float Zoom,
    WorldMapOverlay Overlay,
    WorldMapControls? Controls = null,
    WorldMapAnnotations? Annotations = null,
    WorldMapKind Kind = WorldMapKind.Ancaria,
    WorldMapUiState? Ui = null);

public readonly record struct WorldMapOverlay(
    Vector2 TargetWorldPosition,
    Vector2 TargetScreenPosition,
    bool TargetMarkerVisible,
    bool MinimapVisible,
    string DifficultyDisplayName,
    string RegionDisplayName);
