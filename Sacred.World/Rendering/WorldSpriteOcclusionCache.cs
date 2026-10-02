using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.World.Rendering;

/// <summary>Shares one static-sprite occlusion mask between passes in a sequential offline frame.</summary>
public sealed class WorldSpriteOcclusionCache
{
    private Task<WorldModelOcclusionMask>? _mask;

    /// <summary>Call before rendering a new frame or changing its camera, sprites or indoor state.</summary>
    public void Reset() => _mask = null;

    internal Task<WorldModelOcclusionMask> GetAsync(WorldStaticSpriteProvider sprites, IReadOnlyList<Sector> sectors,
        Vector2 center, int width, int height, float zoom, IndoorTileGroup? activeIndoorGroup) =>
        _mask ??= WorldModelOcclusion.BuildAsync(sprites, sectors, center, width, height, zoom, activeIndoorGroup);
}
