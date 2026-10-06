using System.Collections.Generic;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.GameBin.Scripts;
using Sacred.Engine.Rendering;
using Sacred.World.Map;

namespace Sacred.Engine.Scene.WorldMap;

internal static class WorldMapFrameBuilder
{
    public static ScreenFrame Create(WorldMapAtlas atlas, Vector2 playerWorldPosition, ulong revision,
        WorldMapAnnotations annotations, IReadOnlyDictionary<SacredRegionNpcKind, TextureAsset> npcTextures, bool showNpcs)
    {
        var image = WorldMapComposer.Compose(atlas, playerWorldPosition, annotations, npcTextures, showNpcs);
        return new ScreenFrame(image.Width, image.Height, image.Pixels, revision);
    }
}
