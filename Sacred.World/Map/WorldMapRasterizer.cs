using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.World.Rendering;

namespace Sacred.World.Map;

public sealed class WorldMapRasterizer(TexturePakArchive textures)
{
    public Task<RgbaImage> RenderAsync(Vector2 playerWorldPosition, CancellationToken cancellationToken = default) =>
        RenderAsync(playerWorldPosition, WorldMapKind.Ancaria, cancellationToken: cancellationToken);

    public async Task<RgbaImage> RenderAsync(Vector2 playerWorldPosition, WorldMapKind kind,
        WorldMapAnnotations? annotations = null, bool showNpcs = true, CancellationToken cancellationToken = default)
    {
        var atlas = await new WorldMapAtlasLoader(textures.LoadTextureAsync).LoadAsync(kind, cancellationToken).ConfigureAwait(false);
        var icons = annotations is not null && showNpcs
            ? await WorldMapNpcTextures.LoadAsync(textures.LoadTextureAsync, cancellationToken).ConfigureAwait(false) : null;
        return WorldMapComposer.Compose(atlas, playerWorldPosition, annotations, icons, showNpcs);
    }
}
