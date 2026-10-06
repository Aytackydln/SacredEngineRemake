using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Particles;
using Sacred.World;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace Sacred.Engine.Scene.InGame;

internal sealed class PlayerParticleEffectsController(WorldParticleSystem particles, SceneState scene,
    WorldElevationSampler elevation)
{
    private readonly HashSet<(string TextureName, ParticleTextureMode Mode)> _disabledModelEffects = [];
    private IReadOnlyList<SacredParticleDefinition>? _catalogue;
    private SacredParticleQuality? _quality;
    private bool _cheatSelfTarget = true;
    private bool _cheatFollowSelf;
    private Vector2 _cheatPoint;

    public float SelfHeight => NativeHeight(scene.Debug.ActorTerrainHeight);
    private float NativeHeight(float height) => TerrainElevationProjection.ScreenHeight(height) / (particles.Catalogue.Projection.HeightFactor *
        particles.Catalogue.Projection.VerticalScale);

    public void Update()
    {
        if (scene.Models.Count > 0)
            scene.Models[0].DisabledEquipmentEffects = _disabledModelEffects;
    }

    public PlayerParticlePanelState CreateState(Vector2 position)
    {
        if (_quality != particles.Quality)
        {
            _quality = particles.Quality;
            _catalogue = particles.Catalogue.Definitions.Concat(particles.Catalogue.EventDefinitions)
                .OrderBy(d => d.Status != SacredParticleDefinitionStatus.Decoded)
                .ThenBy(d => d.TypeName, StringComparer.Ordinal).ToArray();
        }
        var enabled = new HashSet<(uint, int?, bool)>();
        foreach (var definition in _catalogue!)
        {
            int? preset = definition.IsEventPreset ? definition.Preset : null;
            if (particles.Effects.IsEnabled(definition.TypeId, true, preset)) enabled.Add((definition.TypeId, preset, true));
            if (particles.Effects.IsEnabled(definition.TypeId, false, preset)) enabled.Add((definition.TypeId, preset, false));
        }
        var effects = scene.Models.FirstOrDefault()?.EquipmentEffects?.Surfaces
            .Select(s => (s.TextureName, s.TextureMode)).Distinct()
            .Select(s => new PlayerModelParticleState(s.TextureName, s.TextureMode, !_disabledModelEffects.Contains(s)))
            .ToArray() ?? [];
        return new(_catalogue!, enabled, effects, position, particles.Effects.ActiveCount);
    }

    public bool Execute(PlayerParticleRequest request, Vector2 position)
    {
        var effects = particles.Effects;
        switch (request.Action)
        {
            case PlayerParticleAction.StopAll:
                effects.Clear();
                break;
            case PlayerParticleAction.EnableModel:
            case PlayerParticleAction.DisableModel:
                if (request.TextureName is null) return false;
                var key = (request.TextureName, request.Mode);
                if (scene.Models.FirstOrDefault()?.EquipmentEffects?.Surfaces.Any(s =>
                    (s.TextureName, s.TextureMode) == key) != true) return false;
                if (request.Action == PlayerParticleAction.DisableModel) _disabledModelEffects.Add(key);
                else _disabledModelEffects.Remove(key);
                Update();
                break;
            case PlayerParticleAction.Disable:
                effects.Disable(request.TypeId, request.SelfTarget, request.EventPreset);
                break;
            default:
                if (!particles.Enabled || !effects.Start(particles.Catalogue, request.TypeId,
                    request.Action == PlayerParticleAction.Enable, request.SelfTarget,
                    request.SelfTarget ? position : request.Point,
                    request.Height + (request.SelfTarget ? 0 : NativeHeight(elevation.SampleHeightOrZero(request.Point))),
                    request.Duration, request.EventPreset, CreateModelVertices(request), request.FollowSelf, SelfHeight))
                {
                    EngineLog.WriteLine($"Particle playback unavailable: 0x{request.TypeId:X} (disabled, unsupported or preview limit reached).");
                    return false;
                }
                break;
        }
        EngineLog.WriteLine($"Debug input: particles {request.Action}, FX 0x{request.TypeId:X}, target={(request.SelfTarget ? "self" : request.Point.ToString())}, follow={request.SelfTarget && request.FollowSelf}, model={request.TextureName}");
        return true;
    }

    private IReadOnlyList<Vector3>? CreateModelVertices(PlayerParticleRequest request)
    {
        if (!request.SelfTarget || request.EventPreset is not { } preset ||
            !particles.Catalogue.TryGetEventDefinition(request.TypeId, preset, out var definition) ||
            definition.ModelBurstCount == 0 || scene.Models.FirstOrDefault() is not { } model) return null;
        return new ModelParticleSeedVertices(model, particles.Catalogue.Projection);
    }

    public bool TrySetCheatOption(string option, string value, Vector2 position, out string message)
    {
        message = string.Empty;
        switch (option.ToLowerInvariant())
        {
            case "particle-crowd":
                if (!int.TryParse(value, out var count) || count is < 0 or > 64)
                {
                    message = "Use set particle-crowd <0..64>.";
                    return true;
                }
                particles.Effects.Clear();
                var stress = particles.Catalogue.Definitions.Where(d =>
                    d.Status == SacredParticleDefinitionStatus.Decoded && d.Draw is not null &&
                    !d.OneTime && d.ModelBurstCount == 0 &&
                    d.ParameterSets.Any(p => p.Emission.EmissionInterval > 0))
                    .OrderByDescending(d => d.Capacity).FirstOrDefault();
                if (stress is null) { message = "No decoded continuous particle effect available."; return true; }
                for (var i = 0; i < count; i++)
                    particles.Effects.Start(particles.Catalogue, stress.TypeId, false, true,
                        position + new Vector2((i % 8 - 3.5f) * .6f, (i / 8 - 3.5f) * .6f),
                        0, emissionSeconds: 600, selfHeight: SelfHeight);
                message = $"particle crowd {count}: {stress.TypeName}, decoded capacity {stress.Capacity}";
                return true;
            case "particle-follow":
                if (value is "on" or "off")
                {
                    _cheatFollowSelf = value == "on";
                    message = $"particle follow player {value}";
                }
                else message = "Use set particle-follow <on|off>.";
                return true;
            case "particle-target":
                if (value.Equals("self", StringComparison.OrdinalIgnoreCase)) _cheatSelfTarget = true;
                else
                {
                    var xy = value.Split(',');
                    if (xy.Length != 2 || !float.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                        !float.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                        !float.IsFinite(x) || !float.IsFinite(y))
                    {
                        message = "Use set particle-target <self|x,y>.";
                        return true;
                    }
                    _cheatSelfTarget = false;
                    _cheatPoint = new(x, y);
                }
                message = $"particle target {(_cheatSelfTarget ? "self" : _cheatPoint.ToString())}";
                return true;
            case "particle-list":
                foreach (var definition in particles.Catalogue.Definitions.Concat(particles.Catalogue.EventDefinitions).OrderBy(d => d.TypeName))
                    if (value == "all" || definition.TypeName.Contains(value, StringComparison.OrdinalIgnoreCase))
                        EngineLog.WriteLine($"FX {definition.TypeName}{(definition.IsEventPreset ? $":{definition.Preset}" : "")} 0x{definition.TypeId:X}: {definition.Status}");
                message = "particle catalogue listed";
                return true;
            case "particle-stop" when value == "all":
                Execute(new(PlayerParticleAction.StopAll), position);
                message = "particle previews stopped";
                return true;
            case "particle-model" when value is "on" or "off":
                foreach (var modelEffect in CreateState(position).ModelEffects)
                    Execute(new(value == "on" ? PlayerParticleAction.EnableModel : PlayerParticleAction.DisableModel,
                        TextureName: modelEffect.TextureName, Mode: modelEffect.Mode), position);
                message = $"current equipment particle effects {value}";
                return true;
            case "particle-play" or "particle-enable" or "particle-disable":
                var parts = value.Split(':');
                int? eventPreset = null;
                if (parts.Length > 2 || (parts.Length == 2 && !int.TryParse(parts[1], out _)))
                {
                    message = "Use an FX name, optionally followed by :<native event subtype>.";
                    return true;
                }
                if (parts.Length == 2) eventPreset = int.Parse(parts[1], CultureInfo.InvariantCulture);
                var definitions = eventPreset is null ? particles.Catalogue.Definitions : particles.Catalogue.EventDefinitions;
                if (eventPreset is null)
                {
                    var namedEvent = particles.Catalogue.EventDefinitions.FirstOrDefault(d =>
                        d.DisplayName?.Equals(value, StringComparison.OrdinalIgnoreCase) == true);
                    if (namedEvent is not null)
                    {
                        eventPreset = namedEvent.Preset;
                        definitions = particles.Catalogue.EventDefinitions;
                        parts[0] = namedEvent.TypeName;
                    }
                }
                var effect = definitions.FirstOrDefault(d => (eventPreset is null || d.Preset == eventPreset) && (
                    d.TypeName.Equals(parts[0], StringComparison.OrdinalIgnoreCase) ||
                    d.TypeName.Equals("TYPE_FX_" + parts[0], StringComparison.OrdinalIgnoreCase)));
                var action = option.ToLowerInvariant() switch
                {
                    "particle-play" => PlayerParticleAction.Play,
                    "particle-enable" => PlayerParticleAction.Enable,
                    _ => PlayerParticleAction.Disable
                };
                var applied = effect is not null && Execute(new(action, effect.TypeId, _cheatSelfTarget, _cheatPoint,
                    EventPreset: eventPreset, FollowSelf: _cheatFollowSelf), position);
                message = applied ? $"particle {action}: {value}" : $"particle unavailable: {value}";
                return true;
            default:
                return false;
        }
    }
}
