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
    private readonly uint? _eventColor;
    private readonly ParticleEmissionLine? _castPath;
    private readonly Vector3 _attachmentPoint;
    private readonly IReadOnlyList<Vector3> _origins = [Vector3.Zero];
    private readonly FxPortalPreview? _portal;
    private readonly FxElementStonePreview? _elementStone;
    private readonly FxForgeElementPreview? _forgeElement;
    private readonly FxPongBatterPreview? _pong;
    private readonly FxAnducarPreview? _anducar;
    private readonly FxDoorSpikePreview? _doorSpike;
    private readonly FxStonedPreview? _stoned;
    private readonly FxLayeredFlarePreview? _layeredFlare;
    private readonly FxPulseFlarePreview? _pulseFlare;
    private readonly FxWarCrySignPreview? _warCrySign;
    private readonly FxRingSurfacePreview? _ringSurface;
    private readonly FxExpandingBurstPreview? _expandingBurst;
    private readonly FxColumnFlarePreview? _columnFlare;
    private readonly FxSpriteFlarePreview? _spriteFlare;
    private readonly FxControlledFlarePreview? _controlledFlare;
    private readonly FxStrikeSymbolPreview? _strikeSymbol;
    private readonly FxRingShieldPreview? _ringShield;
    private readonly FxConfusionPreview? _confusion;
    private readonly FxDeflectionPreview? _deflection;
    private readonly FxUnderworldGlyphPreview? _glyph;
    private readonly FxPreviewDraw[] _drawSlots = [];
    private readonly List<FxPreviewDraw> _draws = [];
    private readonly WorldParticleEffectPlayer _player = new();
    private readonly List<WorldParticle> _particles = [];
    private readonly uint _manualParticleSeed = (uint)Random.Shared.Next();
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
        parameters ??= FxPreviewParameters.Default(definition);
        parameters.Validate();
        if (parameters.UseCastTrail && definition.CastTrail is { } trail)
        {
            var distance = parameters.Distance ?? FxPreviewParameters.DefaultPointDistance;
            CastDistance = distance;
            _origins = FxPreviewCastTrail.Origins(catalogue, definition, distance);
            _castPath = new(Vector3.Zero, FxPreviewCastTrail.NativePoint(catalogue, distance));
            _eventColor = trail.Color;
            definition = FxPreviewCastTrail.Emitter(catalogue, trail);
        }
        _definition = definition;
        if (definition.ExpandingBurst is { } expanding) _expandingBurst = new(expanding, catalogue.Projection);
        _actorBlockRadius = actorBlockRadius;
        _eventStrength = eventStrength;
        _hasActor = actor?.Mesh is not null;
        _attachmentPoint = attachments?.Line is null ? attachments?.Point ?? Vector3.Zero : Vector3.Zero;
        if (definition.Deflection is { } deflection && actor is not null)
        {
            _attachmentPoint = FxPreviewDeflectionInputs.Resolve(actor, deflection, catalogue.Projection);
            _deflection = new(deflection, catalogue.Projection, _attachmentPoint, _manualParticleSeed);
            Mesh = _deflection.Mesh;
            return;
        }
        if (definition.LineEmission is not null && actor is not null)
            _line = parameters.ResolveLine(attachments?.Line ?? FxPreviewLineInputs.Create(actor, catalogue.Projection));
        if (definition.ModelBurstCount > 0 && actor?.Mesh is { } actorMesh)
            _seeds = FxPreviewParticleMesh.NativeSeeds(actorMesh, catalogue.Projection);
        // Empty paths have no draws, but Direct3D buffers still require nonzero storage.
        var capacity = Math.Max(1, checked(_origins.Count * (definition.Capacity * (1 + definition.AdditionalDraws.Count) +
            (definition.Halo is null ? 0 : 1) + definition.AdditionalHalos.Count + (definition.Emperor is null ? 0 : 1))));
        capacity += _expandingBurst?.Capacity ?? 0;
        (Mesh, _drawSlots) = FxPreviewParticleMesh.Create(capacity);
        Start();
        // Cast trails open after the actor bursts have scattered into sparks.
        // Other finite bursts are captured near birth; continuous emitters fill before fitting.
        var steps = _castPath is not null ? 120 : definition.OneTime ? 6 : 120;
        steps = Math.Max(steps, (int)Math.Ceiling((definition.SimulationStartSeconds + .1f) * 60));
        for (var step = 0; step < steps; step++) Advance(1f / 60);
    }

    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredPortalVariant portal)
    {
        _catalogue = catalogue;
        _definition = null!;
        _portal = new(portal, catalogue.Projection);
        Mesh = _portal.Mesh;
    }

    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredElementStoneFxDefinition definition, GrnAsset asset)
    {
        _catalogue = catalogue; _definition = null!;
        _elementStone = new(definition, catalogue.Projection, asset);
        Mesh = _elementStone.Mesh;
    }

    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredForgeElementFxDefinition definition, GrnAsset asset)
    {
        _catalogue = catalogue; _definition = null!;
        _forgeElement = new(definition, catalogue.Projection, asset);
        Mesh = _forgeElement.Mesh;
    }

    public void RequestRelease()
    {
        if (_doorSpike is not null) { _doorSpike.RequestRelease(); Advance(0); return; }
        if (_forgeElement is null) throw new InvalidOperationException("This FX has no decoded release controller.");
        _forgeElement.RequestRelease();
        Advance(0);
    }
    public byte? ForgePhase => _forgeElement?.Phase;
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredStonedFxDefinition definition, GrnAsset asset, string materialTexture)
    {
        _catalogue = catalogue; _definition = null!;
        _stoned = new(definition, catalogue.Projection, asset, materialTexture); Mesh = _stoned.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredDoorSpikeFxDefinition definition, GrnAsset asset)
    {
        _catalogue = catalogue; _definition = null!;
        _doorSpike = new(definition, catalogue.Projection, asset); Mesh = _doorSpike.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredUnderworldGlyphFxDefinition definition, GrnAsset asset)
    {
        _catalogue = catalogue; _definition = null!;
        _glyph = new(definition, catalogue.Projection, asset);
        Mesh = _glyph.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredPongBatterFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _pong = new(definition, catalogue.Projection); Mesh = _pong.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredAnducarFxDefinition definition,
        IReadOnlyList<GrnAsset> models, float distance)
    {
        _catalogue = catalogue; _definition = null!;
        _anducar = new(definition, catalogue.Projection, models, distance); Mesh = _anducar.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredLayeredFlareFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _layeredFlare = new(definition, catalogue.Projection); Mesh = _layeredFlare.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredControlledFlareFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _controlledFlare = new(definition, catalogue.Projection); Mesh = _controlledFlare.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredSpriteFlareFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _spriteFlare = new(definition, catalogue.Projection); Mesh = _spriteFlare.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredPulseFlareFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _pulseFlare = new(definition, catalogue.Projection); Mesh = _pulseFlare.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredBubbleTrapExplosionFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _ringSurface = new(definition, catalogue.Projection); Mesh = _ringSurface.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredTerrorSprinterFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _ringSurface = new(definition, catalogue.Projection); Mesh = _ringSurface.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredWarCrySignFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _warCrySign = new(definition, catalogue.Projection); Mesh = _warCrySign.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredColumnFlareFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _columnFlare = new(definition, catalogue.Projection); Mesh = _columnFlare.Mesh;
    }
    public void SetAtlasCell(int cell) => (_controlledFlare ?? throw new InvalidOperationException("Select a toggle trap first.")).SetAtlasCell(cell);
    public void ExtendDuration(uint units)
    {
        if (_layeredFlare is null) throw new InvalidOperationException("This FX has no decoded timer-extension branch.");
        _layeredFlare.ExtendDuration(units);
        Console.WriteLine($"[Assets] FX native duration extended by {units} timer units.");
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredStrikeSymbolFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _strikeSymbol = new(definition, catalogue.Projection); Mesh = _strikeSymbol.Mesh;
    }
    public void RequestHit()
    {
        if (_deflection is not null) { _deflection.Restart(); return; }
        if ((_definition?.Emperor is not null || _definition?.MineSign is not null) &&
            _player.RequestHit(_definition.TypeId, _definition.Preset!.Value)) return;
        if (_pulseFlare is not null) { _pulseFlare.RequestHit(); return; }
        if (_spriteFlare is not null) { _spriteFlare.RequestRemoval(); return; }
        if (_controlledFlare is not null) { _controlledFlare.RequestToggle(); return; }
        if (_ringShield is not null) { _ringShield.RequestHit(); return; }
        if (_pong is null) throw new InvalidOperationException("This FX has no decoded hit response.");
        _pong.RequestHit();
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredRingShieldFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _ringShield = new(definition, catalogue.Projection); Mesh = _ringShield.Mesh;
    }
    public FxPreviewPlayback(SacredParticleCatalogue catalogue, SacredConfusionFxDefinition definition)
    {
        _catalogue = catalogue; _definition = null!;
        _confusion = new(definition, catalogue.Projection); Mesh = _confusion.Mesh;
    }
    public Mesh Mesh { get; }
    /// <summary>Native orbital envelope for camera fitting before expansion finishes.</summary>
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            if (_stoned is not null)
                foreach (var point in _stoned.CameraFitPoints) yield return point;
            if (_definition?.Plong is { } plong)
                foreach (var point in FxPlongCameraBounds.Points(plong, _catalogue.Projection)) yield return point;
            if (_doorSpike is not null)
                foreach (var point in _doorSpike.CameraFitPoints) yield return point;
            if (_expandingBurst is not null)
                foreach (var point in _expandingBurst.CameraFitPoints) yield return point;
            if (_ringSurface is not null)
                foreach (var point in _ringSurface.CameraFitPoints) yield return point;
            if (_warCrySign is not null)
                foreach (var point in _warCrySign.CameraFitPoints) yield return point;
            if (_pulseFlare is not null)
                foreach (var point in _pulseFlare.CameraFitPoints) yield return point;
            if (_columnFlare is not null)
                foreach (var point in _columnFlare.CameraFitPoints) yield return point;
            if (_confusion is not null)
                foreach (var point in _confusion.CameraFitPoints) yield return point;
            if (_ringShield is not null)
                foreach (var point in _ringShield.CameraFitPoints) yield return point;
            if (_anducar is not null)
                foreach (var point in _anducar.CameraFitPoints) yield return point;
            if (_origins.Count > 1)
                foreach (var origin in _origins) yield return ToModel(origin + _attachmentPoint);
            if (_line is { } line)
            {
                yield return ToModel(line.Start);
                yield return ToModel(line.End);
            }
            if (_castPath is { } path)
            {
                yield return ToModel(path.Start);
                yield return ToModel(path.End);
            }
            foreach (var point in FxPreviewBurstBounds.Points(_definition, _catalogue.Projection, _attachmentPoint, _manualParticleSeed))
                yield return point;
        }
    }
    public bool Playing { get; set; } = true;
    public IReadOnlyList<WorldParticle> Particles => _particles;
    public IReadOnlyList<FxPreviewDraw> Draws => _deflection is { } deflect ? deflect.Draws : _stoned is { } stoned ? stoned.Draws : _doorSpike is { } doorSpike ? doorSpike.Draws : _ringSurface is { } ringSurface ? ringSurface.Draws : _warCrySign is { } sign ? sign.Draws : _pulseFlare is { } pulse ? pulse.Draws : _columnFlare is { } column ? column.Draws : _spriteFlare is { } sprite ? sprite.Draws : _controlledFlare is { } controlled ? controlled.Draws : _portal is { } portal ? portal.Draws : _elementStone is { } stone ? stone.Draws : _forgeElement is { } forge ? forge.Draws : _glyph is { } glyph ? glyph.Draws : _pong is { } pong ? pong.Draws : _anducar is { } anducar ? anducar.Draws : _layeredFlare is { } layered ? layered.Draws : _strikeSymbol is { } strike ? strike.Draws : _ringShield is { } ring ? ring.Draws : _confusion is { } confusion ? confusion.Draws : _draws;
    public bool IsPortal => _portal is not null;
    public bool HasSurfaceGeometry => _stoned is not null || _doorSpike is not null || _expandingBurst is not null || _ringSurface is not null || _warCrySign is not null || _pulseFlare is not null || _columnFlare is not null || _spriteFlare is not null || _controlledFlare is not null || _portal is not null || _elementStone is not null || _forgeElement is not null || _glyph is not null || _pong is not null || _anducar is not null || _layeredFlare is not null || _strikeSymbol is not null || _ringShield is not null || _confusion is not null;
    public Vector3 VertexExtent(int index) => _deflection is not null || _stoned is not null || _doorSpike is not null || _expandingBurst is not null && index >= (_drawSlots.Length - _expandingBurst.Capacity) * 4 || _ringSurface is not null || _warCrySign is not null || _pulseFlare?.IsBillboardVertex(index) == false || _columnFlare is not null || _spriteFlare?.IsBillboardVertex(index) == false || _controlledFlare is not null || IsPortal || _confusion is not null || _ringShield is not null || _layeredFlare is not null || _anducar is not null || _forgeElement is not null || _strikeSymbol?.IsBillboardVertex(index) == false || _elementStone?.IsBillboardVertex(index) == false || _glyph?.IsBillboardVertex(index) == false || _pong?.IsBillboardVertex(index) == false
        ? Vector3.Zero : new Vector3(new Vector2(Mesh.Vertices[index].Normal.X, Mesh.Vertices[index].Normal.Y).Length());
    public ParticleEmissionLine? EmissionLine => _line ?? _castPath;
    public float? CastDistance { get; }
    public IReadOnlyList<Vector3> SampleOrigins => _origins;
    public string PrimitiveDescription => _deflection is not null ? $"Deflection: {_deflection.Draws.Count} line strips ({_deflection.Mesh.Vertices.Length} vertices), active={_deflection.Active}" : _stoned is not null ? $"Stoned: {_stoned.Draws.Count} models, age={_stoned.ElapsedTime:R}, active={_stoned.Active}" : _doorSpike is not null ? $"Door Spike: {_doorSpike.Draws.Count} models, phase={_doorSpike.Phase}, active={_doorSpike.Active}" : _ringSurface is not null ? $"Native ring surfaces: {_ringSurface.Draws.Count} passes, phase={_ringSurface.Phase}, active={_ringSurface.Active}" : _warCrySign is not null ? $"War Cry Sign: {_warCrySign.Draws.Count} passes, phase={_warCrySign.Phase}, active={_warCrySign.Active}" : _pulseFlare is not null ? $"Native flare passes: {_pulseFlare.Draws.Count}, active={_pulseFlare.Active}" : _columnFlare is not null ? $"Column flares: {_columnFlare.Draws.Count}, phase={_columnFlare.Phase}, active={_columnFlare.Active}" : _spriteFlare is not null ? $"Sprite flares: {_spriteFlare.Draws.Count}, active={_spriteFlare.Active}" : _controlledFlare is not null ? $"Controlled ground flares: {_controlledFlare.Draws.Count}, active={_controlledFlare.Active}" : _confusion is not null ? $"2 ground planes, 2 helical ribbons, active={_confusion.Active}" : IsPortal ? "3 portal layers" : _elementStone is not null ? "model, 3 beams, flicker plane, 2 flares"
        : _forgeElement is not null ? $"Forge phase {_forgeElement.Phase}: model, 2 ground flares, cylindrical strip"
        : _glyph is not null ? "2 glyph model passes, 20 ribbons, glow" : _ringShield is not null ? $"Shield surfaces: {_ringShield.Draws.Count}, phase={_ringShield.Phase}, active={_ringShield.Active}" : _strikeSymbol is not null ? $"Strike sprite: frame {_strikeSymbol.TextureFrame}, active={_strikeSymbol.Active}" : _layeredFlare is not null ? $"Ground flares: {_layeredFlare.Draws.Count} layers, active={_layeredFlare.Active}" : _anducar is not null ? $"Anducar: {_anducar.Draws.Count} model passes" : _pong is not null ? "Pong paddle: scrolling box surface and 12 glow lines" : $"{_particles.Count} particles";
    public IReadOnlyList<string> TextureNames => _deflection is not null ? [] : _stoned?.TextureNames ?? _doorSpike?.TextureNames ?? _ringSurface?.TextureNames ?? _warCrySign?.TextureNames ?? _pulseFlare?.TextureNames ?? _columnFlare?.TextureNames ?? _spriteFlare?.TextureNames ?? _controlledFlare?.TextureNames ?? _portal?.TextureNames ?? _elementStone?.TextureNames ?? _forgeElement?.TextureNames ?? _glyph?.TextureNames ?? _pong?.TextureNames ?? _anducar?.TextureNames ?? _layeredFlare?.TextureNames ?? _strikeSymbol?.TextureNames ?? _ringShield?.TextureNames ?? _confusion?.TextureNames ?? new[] { _definition.Draw!.TextureName, _definition.Halo?.TextureName }
        .OfType<string>().Concat(_definition.AdditionalDraws.Select(draw => draw.TextureName))
        .Concat(_definition.AdditionalHalos.Select(halo => halo.TextureName))
        .Concat(_definition.Emperor is { } emperor ? [emperor.FlareTexture] : [])
        .Concat(_expandingBurst?.TextureNames ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    public SacredTextureChannelEncoding Encoding(string texture) => _encodings.GetValueOrDefault(texture);
    public void SetTexture(TextureAsset texture) => _encodings[texture.Name] = SacredTextureChannelAnalyzer.Analyze(texture.Rgba8);

    private void Start()
    {
        if (_deflection is not null) { _deflection.Restart(); return; }
        if (_stoned is not null) { _stoned.Restart(); return; }
        if (_doorSpike is not null) { _doorSpike.Restart(); return; }
        if (_ringSurface is not null) { _ringSurface.Restart(); return; }
        if (_warCrySign is not null) { _warCrySign.Restart(); return; }
        if (_pulseFlare is not null) { _pulseFlare.Restart(); return; }
        if (_columnFlare is not null) { _columnFlare.Restart(); return; }
        if (_spriteFlare is not null) { _spriteFlare.Restart(); return; }
        if (_controlledFlare is not null) { _controlledFlare.Restart(); return; }
        if (_confusion is not null) { _confusion.Restart(); return; }
        if (_portal is not null) { _portal.Restart(); return; }
        if (_elementStone is not null) { _elementStone.Restart(); return; }
        if (_forgeElement is not null) { _forgeElement.Restart(); return; }
        if (_glyph is not null) { _glyph.Restart(); return; }
        if (_pong is not null) { _pong.Restart(); return; }
        if (_anducar is not null) { _anducar.Restart(); return; }
        if (_layeredFlare is not null) { _layeredFlare.Restart(); return; }
        if (_strikeSymbol is not null) { _strikeSymbol.Restart(); return; }
        if (_ringShield is not null) { _ringShield.Restart(); return; }
        _player.Clear();
        _player.ManualParticleSeed = _definition.Plong is null ? null : _manualParticleSeed;
        _expandingBurst?.Restart();
        foreach (var origin in _origins)
        {
            var point = origin + _attachmentPoint;
            if (!_player.Start(_catalogue, _definition.TypeId, !_definition.OneTime, _hasActor,
                    IsometricProjection.IsoToWorld(_catalogue.Projection.Project(new Vector3(point.X, point.Y, 0))),
                    point.Z, eventPreset: _definition.IsEventPreset ? _definition.Preset : null, modelVertices: _seeds,
                    actorBlockRadius: _actorBlockRadius, emissionLine: _line, eventStrength: _eventStrength,
                    eventColor: _eventColor))
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
        if (_deflection is not null) { _deflection.Advance(seconds, !_seeking); return; }
        if (_stoned is not null) { _stoned.Advance(seconds); return; }
        if (_doorSpike is not null) { _doorSpike.Advance(seconds); return; }
        if (_ringSurface is not null) { _ringSurface.Advance(seconds); return; }
        if (_warCrySign is not null) { _warCrySign.Advance(seconds); return; }
        if (_pulseFlare is not null) { _pulseFlare.Advance(seconds); return; }
        if (_columnFlare is not null) { _columnFlare.Advance(seconds); return; }
        if (_spriteFlare is not null) { _spriteFlare.Advance(seconds); return; }
        if (_controlledFlare is not null) { _controlledFlare.Advance(seconds); return; }
        if (_confusion is not null) { _confusion.Advance(seconds); return; }
        if (_portal is not null) { _portal.Advance(seconds); return; }
        if (_elementStone is not null) { _elementStone.Advance(seconds); return; }
        if (_forgeElement is not null) { _forgeElement.Advance(seconds); return; }
        if (_glyph is not null) { _glyph.Advance(seconds); return; }
        if (_pong is not null) { _pong.Advance(seconds); return; }
        if (_anducar is not null) { _anducar.Advance(seconds); return; }
        if (_layeredFlare is not null) { _layeredFlare.Advance(seconds); return; }
        if (_strikeSymbol is not null) { _strikeSymbol.Advance(seconds); return; }
        if (_ringShield is not null) { _ringShield.Advance(seconds); return; }
        _burstElapsed += seconds;
        // Replay is a viewer control; each native burst remains finite and retains its authored lifetime.
        if (!_seeking && _definition.OneTime && _player.ActiveCount == 0 && _burstElapsed >= 2) Start();
        _particles.Clear();
        _player.Advance(seconds, _particles);
        _expandingBurst?.Advance(seconds);
        _draws.Clear();
        for (var index = 0; index < _particles.Count; index++)
        {
            var draw = _drawSlots[index];
            FxPreviewParticleMesh.Write(_particles[index], draw, Mesh, index);
            _draws.Add(draw);
        }
        _expandingBurst?.Append(Mesh, _drawSlots.Length - _expandingBurst.Capacity, _draws);
        Mesh.NotifyVerticesChanged();
    }
}
