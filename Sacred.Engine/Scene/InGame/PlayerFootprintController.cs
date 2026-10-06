using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Rendering;
using Sacred.World;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

internal sealed class PlayerFootprintController
{
    private readonly FootprintTrail _trail = new();
    private readonly FootprintSpriteLoader _textures;
    private readonly WorldStreamer _world;
    private readonly SceneState _scene;
    private readonly WorldElevationSampler _elevation;
    private readonly List<TerrainStaticSprite> _sprites = new(FootprintTrail.Capacity);
    private readonly Dictionary<Vector2, float> _heights = [];
    private readonly HashSet<Vector2> _livePositions = [];
    private readonly List<Vector2> _expiredPositions = new(FootprintTrail.Capacity);
    private IndoorTileGroup? _floor;
    public bool Enabled { get; set; } = true;

    public PlayerFootprintController(AssetManager assets, WorldStreamer world, SceneState scene)
    {
        _textures = new(assets);
        _world = world;
        _scene = scene;
        _elevation = new(world);
        foreach (var kind in new[] { TerrainFootprintKind.Desert, TerrainFootprintKind.Snow,
            TerrainFootprintKind.Swamp, TerrainFootprintKind.Water }) _textures.GetOrRequest(kind);
    }

    public string Status => $"footprints {(Enabled ? "on" : "off")}: {_trail.Contacts.Count} contacts, {_sprites.Count} sprites" +
        (_trail.Contacts.Count > 0 && _heights.TryGetValue(_trail.Contacts[0].Position, out var height)
            ? $"; oldest {_trail.Contacts[0].Position}, height {height:F2}, opacity {_trail.Contacts[0].Opacity:F2}" : string.Empty);
    public void Reset(Vector2 position) => _trail.ResetMovement(position);
    public void Clear() { _trail.Clear(); _sprites.Clear(); _heights.Clear(); _scene.FootprintRevision++; }

    public void Update(float seconds, SacredCamera camera)
    {
        if (!ReferenceEquals(_floor, _scene.Indoor.ActiveGroup))
        {
            Clear();
            _floor = _scene.Indoor.ActiveGroup;
            Reset(camera.WorldCenter);
        }
        _trail.Update(seconds, camera.WorldCenter, Enabled && camera.CurrentMovementSpeed > 0, SampleKind);
        if (_sprites.Count == 0 && _trail.Contacts.Count == 0) return;
        _sprites.Clear();
        _livePositions.Clear();
        foreach (var contact in _trail.Contacts)
        {
            _livePositions.Add(contact.Position);
            if (!_heights.ContainsKey(contact.Position))
                _heights[contact.Position] = _elevation.SampleHeightOrZero(contact.Position);
        }
        _expiredPositions.Clear();
        foreach (var position in _heights.Keys)
            if (!_livePositions.Contains(position)) _expiredPositions.Add(position);
        foreach (var position in _expiredPositions) _heights.Remove(position);
        if (Enabled)
        foreach (var contact in _trail.Contacts)
        {
            var texture = _textures.GetOrRequest(contact.Kind);
            if (texture is null) continue;
            var pos = contact.Position;
            var anchor = IsometricProjection.WorldToIso(pos.X, pos.Y) + IsometricProjection.TileAnchorOffset;
            var height = TerrainElevationProjection.ScreenHeight(_heights[pos]);
            // Native render rotates a planar quad then halves its vertical projection.
            var x = (int)MathF.Floor(pos.X);
            var y = (int)MathF.Floor(pos.Y);
            var angle = MathF.Atan2(contact.Direction.X + contact.Direction.Y,
                contact.Direction.X - contact.Direction.Y) + MathF.PI * 0.5f;
            _sprites.Add(new TerrainStaticSprite(texture, uint.MaxValue - 1, false, true,
                false, false, false, null, false, texture.Width, texture.Height,
                anchor.X - texture.Width * .5f, anchor.Y - height - texture.Height * .5f,
                anchor.X, anchor.Y, (short)(_floor?.SurfaceRenderLayer ?? 1), _floor is not null,
                (int)WorldRenderLayer.Objects, x + y, y, x, -1, _sprites.Count, contact.Opacity)
            {
                ParticleAtlasCell = contact.Frame,
                IsGroundDecal = true,
                ParticleRotation = angle,
                ParticleBlendFlags = 8u,
                ParticleDepthKey = WorldPainterDepth.FromWorld(pos)
            });
        }
        _scene.FootprintSprites = _sprites;
        _scene.FootprintRevision++;
    }

    private TerrainFootprintKind SampleKind(Vector2 pos)
    {
        var x = (int)MathF.Floor(pos.X);
        var y = (int)MathF.Floor(pos.Y);
        // Native setFootstep selects the outdoor patch even when height is
        // resolved on an indoor floor; don't infer a material from a sound slot.
        foreach (var sector in _world.VisibleWorld.Sectors)
        {
            var lx = x - sector.Coord.X * Sector.TileCount;
            var ly = y - sector.Coord.Y * Sector.TileCount;
            if ((uint)lx < Sector.TileCount && (uint)ly < Sector.TileCount)
                return sector.Pathing[lx, ly].Properties.FootprintKind;
        }
        return TerrainFootprintKind.None;
    }
}
