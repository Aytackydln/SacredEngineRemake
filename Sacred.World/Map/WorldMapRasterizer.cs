using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.World.Rendering;

namespace Sacred.World.Map;

public sealed class WorldMapRasterizer(TexturePakArchive textures)
{
    public async Task<RgbaImage> RenderAsync(Vector2 playerWorldPosition, CancellationToken cancellationToken = default)
    {
        var atlas = await new WorldMapAtlasLoader(textures.LoadTextureAsync).LoadAsync(cancellationToken)
            .ConfigureAwait(false);
        var canvas = new RgbaCanvas(atlas.Width, atlas.Height, 0, 0, 0);
        var atlasTexture = new TextureAsset("Ancaria world map", atlas.Width, atlas.Height, atlas.Rgba);
        canvas.DrawTexture(atlasTexture, 0, 0, atlas.Width, atlas.Height);
        var markerPosition = WorldMapProjection.WorldToMap(playerWorldPosition, atlas.Width);
        canvas.DrawTexture(
            atlas.PlayerMarker,
            markerPosition.X - atlas.PlayerMarker.Width,
            markerPosition.Y - atlas.PlayerMarker.Height,
            atlas.PlayerMarker.Width * 2,
            atlas.PlayerMarker.Height * 2);
        return canvas.ToImage();
    }
}
