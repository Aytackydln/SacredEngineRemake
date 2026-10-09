using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.World.Portals;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Adapts native world particles to the viewer's model coordinate space.</summary>
internal sealed class FxPreviewPlayback
{
    private readonly SacredParticleCatalogue _catalogue;
    private readonly SacredParticleDefinition _definition;
    private readonly Vector3[]? _seeds;
    private readonly bool _hasActor;
    private readonly uint? _actorBlockRadius;
    private readonly ParticleEmissionLine? _line;
    private readonly uint _eventStrength;
    private readonly Vector3 _attachmentPoint;
    private readonly IReadOnlyList<Vector3> _origins = [Vector3.Zero];
    private readonly FxPortalPreview? _portal;
    private readonly FxPreviewDraw[] _drawSlots = [];
    private readonly List<FxPreviewDraw> _draws = [];
    private readonly WorldParticleEffectPlayer _player = new();
    private readonly List<WorldParticle> _particles = [];
    private readonly Dictionary<string, SacredTextureChannelEncoding> _encodings = new(StringComparer.OrdinalIgnoreCase);
    private float _burstElapsed;
    private bool _seeking;

    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredParticleDefinition definition, GrnAsset? actor)
        : this(catalogue, definition, actor, null) { }

    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredParticleDefinition definition, GrnAsset? actor,
        uint? actorBlockRadius, uint eventStrength = 0, FxPreviewAttachmentInputs? attachments = null,
        FxPreviewParameters? parameters = null)
    {
        _catalogue = catalogue;
        _definition = definition;
        parameters ??= FxPreviewParameters.Default(definition);
        parameters.Validate();
        _origins = parameters.Origins(definition);
        _actorBlockRadius = actorBlockRadius;
        _eventStrength = eventStrength;
        _hasActor = actor?.Mesh is not null;
        _attachmentPoint = attachments?.Line is null ? attachments?.Point ?? Vector3.Zero : Vector3.Zero;
        if (definition.LineEmission is not null && actor is not null)
            _line = parameters.ResolveLine(attachments?.Line ?? FxPreviewLineInputs.Create(actor, catalogue.Projection));
        if (definition.ModelBurstCount > 0 && actor?.Mesh is { } actorMesh)
        {
            var projection = catalogue.Projection;
            _seeds = actorMesh.Vertices.Select(vertex => new Vector3(
                vertex.Position.X / projection.HorizontalScale,
                -vertex.Position.Y / (MathF.Sqrt(2) * projection.GroundDepthFactor * projection.VerticalScale),
                vertex.Position.Z / (MathF.Sqrt(2) * projection.HeightFactor * projection.VerticalScale))).ToArray();
        }
        var capacity = checked(_origins.Count * (definition.Capacity * (1 + definition.AdditionalDraws.Count) +
            (definition.Halo is null ? 0 : 1) + definition.AdditionalHalos.Count));
        if (capacity > ushort.MaxValue / 4) throw new NotSupportedException("FX exceeds the preview index capacity.");
        var indices = new ushort[capacity * 6];
        for (var index = 0; index < capacity; index++)
        {
            var vertex = index * 4;
            var start = index * 6;
            indices[start] = (ushort)vertex; indices[start + 1] = (ushort)(vertex + 1);
            indices[start + 2] = (ushort)(vertex + 2); indices[start + 3] = (ushort)(vertex + 2);
            indices[start + 4] = (ushort)(vertex + 1); indices[start + 5] = (ushort)(vertex + 3);
        }
        Mesh = new(new VertexPositionNormalTexture[capacity * 4], indices);
        _drawSlots = Enumerable.Range(0, capacity).Select(index => new FxPreviewDraw
            { IndexStart = index * 6, IndexCount = 6 }).ToArray();
        Start();
        // Finite bursts are captured near birth; continuous emitters fill before camera fitting.
        var steps = definition.OneTime ? 6 : 120;
        for (var step = 0; step < steps; step++) Advance(1f / 60);
    }

    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredPortalVariant portal)
    {
        _catalogue = catalogue;
        _definition = null!;
        _portal = new(portal, catalogue.Projection);
        Mesh = _portal.Mesh;
    }

    public Mesh Mesh { get; }
    /// <summary>Native orbital envelope for camera fitting before expansion finishes.</summary>
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            if (_origins.Count > 1)
                foreach (var origin in _origins) yield return ToModel(origin + _attachmentPoint);
            if (_line is { } line)
            {
                yield return ToModel(line.Start);
                yield return ToModel(line.End);
            }
            if (_definition?.Teleport is not { } teleport) yield break;
            var projection = _catalogue.Projection;
            var extent = teleport.OrbitEnergy * Math.Max(projection.HorizontalScale, projection.VerticalScale);
            foreach (var x in new[] { -teleport.OrbitRadius, teleport.OrbitRadius })
            foreach (var y in new[] { -teleport.OrbitRadius, teleport.OrbitRadius })
            foreach (var z in new[] { 0f, 2 * teleport.BaseHeight })
            {
                var ground = IsometricProjection.IsoToWorld(projection.Project(new(x, y, 0)));
                var center = new Vector3(IsometricProjection.WorldToModel(ground.X, ground.Y),
                    z * projection.HeightFactor * projection.VerticalScale * MathF.Sqrt(2));
                yield return center - new Vector3(extent);
                yield return center + new Vector3(extent);
            }
        }
    }
    public bool Playing { get; set; } = true;
    public IReadOnlyList<WorldParticle> Particles => _particles;
    public IReadOnlyList<FxPreviewDraw> Draws => _portal is { } portal ? portal.Draws : _draws;
    public bool IsPortal => _portal is not null;
    public ParticleEmissionLine? EmissionLine => _line;
    public IReadOnlyList<Vector3> SampleOrigins => _origins;
    public string PrimitiveDescription => IsPortal ? "3 portal layers" : $"{_particles.Count} particles";
    public IReadOnlyList<string> TextureNames => _portal?.TextureNames ?? new[] { _definition.Draw!.TextureName, _definition.Halo?.TextureName }
        .OfType<string>().Concat(_definition.AdditionalDraws.Select(draw => draw.TextureName))
        .Concat(_definition.AdditionalHalos.Select(halo => halo.TextureName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public SacredTextureChannelEncoding Encoding(string texture) => _encodings.GetValueOrDefault(texture);
    public void SetTexture(TextureAsset texture) => _encodings[texture.Name] = SacredTextureChannelAnalyzer.Analyze(texture.Rgba8);

    private void Start()
    {
        if (_portal is not null) { _portal.Restart(); return; }
        _player.Clear();
        foreach (var origin in _origins)
        {
            var point = origin + _attachmentPoint;
            if (!_player.Start(_catalogue, _definition.TypeId, !_definition.OneTime, _hasActor,
                    IsometricProjection.IsoToWorld(_catalogue.Projection.Project(new Vector3(point.X, point.Y, 0))),
                    point.Z, eventPreset: _definition.IsEventPreset ? _definition.Preset : null, modelVertices: _seeds,
                    actorBlockRadius: _actorBlockRadius, emissionLine: _line, eventStrength: _eventStrength))
                throw new NotSupportedException("The native effect requires inputs that are unavailable in this preview.");
        }
        _burstElapsed = 0;
    }

    private Vector3 ToModel(Vector3 point)
    {
        var projection = _catalogue.Projection;
        var ground = IsometricProjection.IsoToWorld(projection.Project(new(point.X, point.Y, 0)));
        return new(IsometricProjection.WorldToModel(ground.X, ground.Y),
            point.Z * projection.HeightFactor * projection.VerticalScale * MathF.Sqrt(2));
    }

    public void Restart() { Start(); Advance(0); }

    public void SetTime(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || seconds > 60)
            throw new ArgumentOutOfRangeException(nameof(seconds), "FX preview time must be between 0 and 60 seconds.");
        _seeking = true;
        try
        {
            Start();
            while (seconds > 0)
            {
                var step = Math.Min(seconds, 1f / 60);
                Advance(step);
                seconds -= step;
            }
            Advance(0);
        }
        finally { _seeking = false; }
        Playing = false;
    }

    public void Advance(float seconds)
    {
        if (_portal is not null) { _portal.Advance(seconds); return; }
        _burstElapsed += seconds;
        // Replay is a viewer control; each native burst remains finite and retains its authored lifetime.
        if (!_seeking && _definition.OneTime && _player.ActiveCount == 0 && _burstElapsed >= 2) Start();
        _particles.Clear();
        _player.Advance(seconds, _particles);
        _draws.Clear();
        for (var index = 0; index < _particles.Count; index++)
        {
            var particle = _particles[index];
            var draw = _drawSlots[index];
            draw.TextureName = particle.Sprite.TextureName;
            draw.Additive = particle.Additive;
            draw.SourceColorOnly = particle.SourceColorOnly;
            var color = particle.Color;
            draw.UsesCornerTint = particle.CornerColors is not null;
            if (draw.UsesCornerTint) color = (color & 0xFF000000) | 0xFFFFFF;
            draw.Color = new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f,
                (color & 255) / 255f, (color >> 24) / 255f);
            _draws.Add(draw);
            var ground = IsometricProjection.WorldToModel(particle.WorldX, particle.WorldY);
            var center = new Vector3(ground, particle.Height * MathF.Sqrt(2));
            var halfWidth = particle.Size * .5f;
            var halfHeight = particle.RenderHeight * .5f;
            var sine = MathF.Sin(particle.Rotation); var cosine = MathF.Cos(particle.Rotation);
            var side = particle.Sprite.AtlasColumns;
            var cell = particle.AtlasCell;
            for (var corner = 0; corner < 4; corner++)
            {
                var u = corner / 2; var v = corner % 2;
                var x = (u * 2 - 1) * halfWidth; var y = (v * 2 - 1) * halfHeight;
                Mesh.Vertices[index * 4 + corner] = new(center,
                    new(x * cosine - y * sine, x * sine + y * cosine,
                        particle.CornerColors is { } colors ? (colors[corner] & 0xFFFFFF) + 1 : 1),
                    new((cell % side + u) / (float)side, (cell / side + 1 - v) / (float)particle.Sprite.AtlasRows));
            }
        }
        Mesh.NotifyVerticesChanged();
    }
}
