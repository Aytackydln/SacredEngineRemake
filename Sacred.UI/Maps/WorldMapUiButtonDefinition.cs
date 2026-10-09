using System.Numerics;
using Sacred.UI.Textures;

namespace Sacred.UI.Maps;

public sealed record WorldMapUiButtonDefinition(WorldMapUiButton Button, UiTextureRegion Up,
    UiTextureRegion Down, Vector2 Position, bool Enabled);
