using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.GameBin.Scripts;
using Sacred.World.Rendering;

namespace Sacred.World.Map;

public static class WorldMapComposer
{
    public static RgbaImage Compose(WorldMapAtlas atlas, Vector2 playerWorldPosition,
        WorldMapAnnotations? annotations = null,
        IReadOnlyDictionary<SacredRegionNpcKind, TextureAsset>? npcTextures = null, bool showNpcs = true)
    {
        var canvas = new RgbaCanvas(atlas.Width, atlas.Height, 0, 0, 0);
        atlas.Rgba.CopyTo(canvas.Pixels, 0);
        if (showNpcs && annotations is not null && npcTextures is not null)
        {
            foreach (var npc in annotations.Npcs)
            {
                var position = WorldMapProjection.WorldToMap(new(npc.TileX, npc.TileY), atlas.Width, atlas.Kind);
                if (!WorldMapProjection.Contains(position, atlas) || !npcTextures.TryGetValue(npc.Kind, out var texture)) continue;
                // Native renderIcons draws each service symbol in a 16x16 map rectangle.
                canvas.DrawTexture(texture, position.X - 8, position.Y - 8, 16, 16);
            }
        }
        var marker = WorldMapProjection.WorldToMap(playerWorldPosition, atlas.Width, atlas.Kind);
        if (WorldMapProjection.Contains(marker, atlas))
            canvas.DrawTexture(atlas.PlayerMarker, marker.X - atlas.PlayerMarker.Width,
                marker.Y - atlas.PlayerMarker.Height, atlas.PlayerMarker.Width * 2, atlas.PlayerMarker.Height * 2);
        return canvas.ToImage();
    }
}
