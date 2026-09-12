using System;
using System.Collections.Generic;
using Sacred.Engine.Assets;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace Sacred.Engine.Rendering;

/// <summary>Projects live FunkCode particle instances into the world sprite pass.</summary>
internal sealed class TerrainParticleSpriteBuilder(AssetManager assets)
{
    private readonly List<TerrainStaticSprite> _sprites = new(512);
    private bool _assetRequestsPending = true;

    public IReadOnlyList<TerrainStaticSprite> Sprites => _sprites;
    public bool HasPendingAssetRequests => _assetRequestsPending;

    public void Prepare(IReadOnlyList<WorldParticle> particles)
    {
        _sprites.Clear();
        var requestsPending = false;

        foreach (var particle in particles)
        {
            if (!assets.TryGetWorldParticleSpriteOrRequest(particle.Sprite, out var sprite))
            {
                requestsPending = true;
                continue;
            }
            if (sprite is null)
                continue;

            var anchor = IsometricProjection.WorldToIso(particle.WorldX, particle.WorldY) +
                         IsometricProjection.TileAnchorOffset;
            var size = particle.Size;
            var height = particle.RenderHeight;
            var tileWorldX = (int)MathF.Floor(particle.WorldX);
            var tileWorldY = (int)MathF.Floor(particle.WorldY);
            _sprites.Add(new TerrainStaticSprite(
                sprite,
                uint.MaxValue,
                true,
                true,
                false,
                false,
                false,
                null,
                false,
                size,
                height,
                anchor.X - size * 0.5f,
                anchor.Y - particle.Height - height * 0.5f,
                anchor.X,
                anchor.Y,
                1,
                false,
                4,
                tileWorldX + tileWorldY,
                tileWorldY,
                tileWorldX,
                0,
                particle.DrawOrder,
                particle.Opacity)
            {
                ParticleColor = particle.Color,
                ParticleAtlasCell = particle.AtlasCell,
                ParticleRotation = particle.Rotation,
                ParticleBlendFlags = (particle.Additive ? 2u : 0u) | (particle.SourceColorOnly ? 4u : 0u)
            });
        }

        _sprites.Sort(static (left, right) =>
        {
            var depth = left.TileDepth.CompareTo(right.TileDepth);
            if (depth != 0)
                return depth;
            var y = left.TileWorldY.CompareTo(right.TileWorldY);
            if (y != 0)
                return y;
            var x = left.TileWorldX.CompareTo(right.TileWorldX);
            return x != 0 ? x : left.InsertionOrder.CompareTo(right.InsertionOrder);
        });
        _assetRequestsPending = requestsPending;
    }
}
