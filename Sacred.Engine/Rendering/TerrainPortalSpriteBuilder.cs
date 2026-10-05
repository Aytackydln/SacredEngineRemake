using System.Collections.Generic;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Portals;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Particles;
using Sacred.World.Geometry;
using Sacred.World.Portals;

namespace Sacred.Engine.Rendering;

internal sealed class TerrainPortalSpriteBuilder(AssetManager assets)
{
    private readonly PortalSpriteLoader _textures = new(assets);
    private readonly List<TerrainStaticSprite> _sprites = [];
    private readonly SacredParticleProjection _projection = SacredParticleCatalogue.LoadEmbedded().Projection;
    private bool _prepared;
    private bool _wasPending;
    private IndoorTileGroup? _floor;
    public IReadOnlyList<TerrainStaticSprite> Sprites => _sprites;
    public bool HasPendingAssetRequests => _textures.Pending;

    public bool Prepare(WorldPortalScriptIndex portals, IReadOnlyList<Sector> sectors,
        IndoorTileGroup? floor, bool worldChanged)
    {
        if (_prepared && !worldChanged && ReferenceEquals(_floor, floor) && !_wasPending) return false;
        _prepared = true;
        _floor = floor;
        _sprites.Clear();
        foreach (var sector in sectors)
        foreach (var portal in portals.GetBillboards(sector.Coord))
        {
            if (floor is not null || portal.SurfaceLevel != 0) continue;
            var textures = _textures.GetOrRequest();
            if (textures is null) continue;
            var anchor = IsometricProjection.WorldToIso(portal.Position) + IsometricProjection.TileAnchorOffset;
            var heightScale = _projection.HeightFactor * _projection.VerticalScale;
            var centerHeight = (SacredPortalAppearance.CenterHeight + portal.HeightOffset) * heightScale;
            var centerY = anchor.Y - centerHeight;
            Add(textures[0], SacredPortalAppearance.SurfaceWidth * _projection.HorizontalScale,
                SacredPortalAppearance.SurfaceHeight * heightScale, 64, 1);
            Add(textures[1], 2 * SacredPortalAppearance.WhirlHalfWidth * _projection.HorizontalScale,
                2 * SacredPortalAppearance.WhirlHalfHeight * heightScale, 16, SacredPortalAppearance.FirstWhirlOpacity);
            Add(textures[2], 2 * SacredPortalAppearance.WhirlHalfWidth * _projection.HorizontalScale,
                2 * SacredPortalAppearance.WhirlHalfHeight * heightScale, 32 | 2, SacredPortalAppearance.SecondWhirlOpacity);

            void Add(StaticSpriteAsset texture, float width, float height, uint flags, float opacity)
            {
                _sprites.Add(new(texture, uint.MaxValue - 2, true, true, false, false, false, null, false,
                    width, height, anchor.X - width * .5f, centerY - height * .5f, anchor.X, anchor.Y,
                    1, false, 3, portal.TileX + portal.TileY, portal.TileY, portal.TileX,
                    int.MaxValue, _sprites.Count, opacity)
                {
                    IsPortalBillboard = true,
                    ParticleBlendFlags = flags,
                    ParticleAtlasCell = portal.ScriptOffset,
                    ParticleDepthKey = WorldPainterDepth.FromWorld(portal.Position)
                });
            }
        }
        _wasPending = _textures.Pending;
        return true;
    }
}
