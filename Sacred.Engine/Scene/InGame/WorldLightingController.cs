using System;
using System.Numerics;
using Sacred.Core.World.Sector;

namespace Sacred.Engine.Scene.InGame;

public enum WorldLightingMode
{
    Day,
    Night,
    TimedDayNightCycle,
    PitchBlack,
}

/// <summary>Applies deterministic lighting profiles, including the world-quad ambient level.</summary>
public sealed class WorldLightingController
{
    private static readonly Vector3 DayAmbientColor = new(1.00f, 1.00f, 0.95f);
    private static readonly float DayAmbientIntensity = 0.35f;
    
    private static readonly Vector3 DayLightColor = new(1.0f, 0.93f, 0.82f);
    private static readonly float DayDiffuseIntensity = 1.00f;

    private static readonly float DaySpecularIntensity = 0.12f;

    private const float DayDurationSeconds = 15.0f;
    private const float NightDurationSeconds = 10.0f;
    private const float TransitionDurationSeconds = 5.0f;
    private const float IndoorContactShadowOpacity = 0.65f;

    private const float CycleDurationSeconds =
        DayDurationSeconds + NightDurationSeconds + TransitionDurationSeconds * 2.0f;
    private float _cycleElapsedSeconds;
    private bool _zoneInitialized;
    private float? _networkDayFraction;
    private float? _debugDayFraction;
    private readonly float _nightDarkness;

    public void SetDebugDayFraction(float? fraction)
    {
        if (fraction is { } value && (!float.IsFinite(value) || value < 0 || value >= 1))
            throw new ArgumentOutOfRangeException(nameof(fraction));
        _debugDayFraction = fraction;
    }

    public void SetNetworkDayFraction(float? fraction)
    {
        if (fraction is { } value && (!float.IsFinite(value) || value < 0 || value >= 1))
            throw new ArgumentOutOfRangeException(nameof(fraction));
        _networkDayFraction = fraction;
    }

    public WorldLightingController(WorldLightingMode mode = WorldLightingMode.TimedDayNightCycle, float nightDarkness = 0.5f)
    {
        if (!float.IsFinite(nightDarkness) || nightDarkness < 0 || nightDarkness > 1)
            throw new ArgumentOutOfRangeException(nameof(nightDarkness));
        _nightDarkness = nightDarkness;
        Mode = mode;
        ResetClock();
    }

    public WorldLightingMode Mode { get; private set; }
    public WorldZone CurrentZone { get; private set; } = WorldZone.Outdoors;

    /// <summary>Outdoor calendar position for the HUD; cave ambient lighting does not change the clock.</summary>
    public float DayFraction
    {
        get
        {
            if ((_debugDayFraction ?? _networkDayFraction) is { } fraction) return fraction;
            if (Mode == WorldLightingMode.Day) return 0.5f;
            if (Mode is WorldLightingMode.Night or WorldLightingMode.PitchBlack) return 0;
            // Keep the accelerated preview durations, mapped to native calendar phases.
            var elapsed = _cycleElapsedSeconds;
            if (elapsed < DayDurationSeconds)
                return (WorldDayNightLighting.DawnEnd +
                    (WorldDayNightLighting.DuskStart - WorldDayNightLighting.DawnEnd) * elapsed / DayDurationSeconds) /
                    WorldDayNightLighting.MinutesPerDay;
            elapsed -= DayDurationSeconds;
            if (elapsed < TransitionDurationSeconds)
                return (WorldDayNightLighting.DuskStart +
                    (WorldDayNightLighting.DuskEnd - WorldDayNightLighting.DuskStart) * elapsed / TransitionDurationSeconds) /
                    WorldDayNightLighting.MinutesPerDay;
            elapsed -= TransitionDurationSeconds;
            if (elapsed < NightDurationSeconds)
                return ((WorldDayNightLighting.DuskEnd +
                    (WorldDayNightLighting.MinutesPerDay - WorldDayNightLighting.DuskEnd + WorldDayNightLighting.DawnStart) *
                    elapsed / NightDurationSeconds) / WorldDayNightLighting.MinutesPerDay) % 1;
            elapsed -= NightDurationSeconds;
            return (WorldDayNightLighting.DawnStart +
                (WorldDayNightLighting.DawnEnd - WorldDayNightLighting.DawnStart) * elapsed / TransitionDurationSeconds) /
                WorldDayNightLighting.MinutesPerDay;
        }
    }

    public void CycleMode()
    {
        Mode = Mode switch
        {
            WorldLightingMode.Day => WorldLightingMode.Night,
            WorldLightingMode.Night => WorldLightingMode.PitchBlack,
            WorldLightingMode.PitchBlack => WorldLightingMode.TimedDayNightCycle,
            WorldLightingMode.TimedDayNightCycle => WorldLightingMode.Day,
            _ => WorldLightingMode.Day,
        };
        ResetClock();
    }

    public void SetMode(WorldLightingMode mode)
    {
        Mode = mode;
        ResetClock();
    }

    /// <returns><see langword="true"/> when the visible lighting description changes.</returns>
    public bool Update(
        float elapsedSeconds,
        SceneLighting lighting,
        WorldZone zone)
    {
        var previousPhase = GetCyclePhase();
        var zoneChanged = !_zoneInitialized || CurrentZone != zone;
        CurrentZone = zone;
        _zoneInitialized = true;
        lighting.IndoorShadowOpacity = 0.0f;
        if (Mode == WorldLightingMode.TimedDayNightCycle)
        {
            _cycleElapsedSeconds = (_cycleElapsedSeconds + MathF.Max(0.0f, elapsedSeconds)) %
                                   CycleDurationSeconds;
        }

        if (zone == WorldZone.Cave)
        {
            ApplyCaveProfile(lighting);
            ApplyCelestialLighting(lighting);
            lighting.ShadowMode = SceneShadowMode.None;
            lighting.ShadowOpacity = 0.0f;
            lighting.OutdoorShadowOpacity = 0.0f;
        }
        else if (_debugDayFraction is null && _networkDayFraction is null && Mode == WorldLightingMode.PitchBlack)
        {
            ApplyPitchBlack(lighting);
            ApplyCelestialLighting(lighting);
        }
        else
        {
            ApplyProfile(GetNightBlend(), lighting);
            ApplyCelestialLighting(lighting);
            if (zone == WorldZone.Indoors)
            {
                lighting.ShadowMode = SceneShadowMode.SoftContact;
                lighting.ShadowOpacity = IndoorContactShadowOpacity;
                lighting.IndoorShadowOpacity = IndoorContactShadowOpacity;
            }
        }

        if (zoneChanged)
        {
            EngineLog.WriteLine(
                $"Lighting zone: {zone}; surface shadows outdoor={lighting.OutdoorShadowOpacity:0.00}, " +
                $"indoor={lighting.IndoorShadowOpacity:0.00}");
        }

        return zoneChanged ||
               Mode == WorldLightingMode.TimedDayNightCycle && previousPhase != GetCyclePhase();
    }

    public string DisplayName => CurrentZone switch
    {
        WorldZone.Cave => "Cave: Night",
        WorldZone.Indoors => $"Indoors: {ModeDisplayName}",
        _ => ModeDisplayName,
    };

    private string ModeDisplayName => _debugDayFraction is { } debug ? $"Preview: {debug * 24:0.00}h" :
        _networkDayFraction is { } fraction ? $"Network: {fraction * 24:0.00}h" : Mode switch
    {
        WorldLightingMode.Day => "Day",
        WorldLightingMode.Night => "Night",
        WorldLightingMode.PitchBlack => "Pitch Black",
        _ => GetCyclePhase() switch
        {
            LightingCyclePhase.Day => "Cycle: Day (15s)",
            LightingCyclePhase.Dusk => "Cycle: Dusk (5s)",
            LightingCyclePhase.Night => "Cycle: Night (10s)",
            _ => "Cycle: Dawn (5s)"
        }
    };

    private float GetNightBlend() => WorldDayNightLighting.GetNightValue(DayFraction) / 255f;

    private LightingCyclePhase GetCyclePhase()
    {
        if (Mode != WorldLightingMode.TimedDayNightCycle)
            return Mode == WorldLightingMode.Night ? LightingCyclePhase.Night : LightingCyclePhase.Day;

        if (_cycleElapsedSeconds < DayDurationSeconds)
            return LightingCyclePhase.Day;
        if (_cycleElapsedSeconds < DayDurationSeconds + TransitionDurationSeconds)
            return LightingCyclePhase.Dusk;
        if (_cycleElapsedSeconds < DayDurationSeconds + TransitionDurationSeconds + NightDurationSeconds)
            return LightingCyclePhase.Night;
        return LightingCyclePhase.Dawn;
    }

    private void ApplyProfile(float nightBlend, SceneLighting lighting)
    {
        var blend = Math.Clamp(nightBlend, 0.0f, 1.0f);
        var surfaceColour = WorldDayNightLighting.GetSurfaceColour(
            (int)MathF.Round(blend * 255), _nightDarkness);
        // Native hilight_getIntensity_wp (0x41B3C0) exposes the same palette RGB
        // to models. Retain the renderer's daytime shading, modulated by that
        // filter, instead of introducing a separate blue moonlight/specular ramp.
        lighting.LightColor = DayLightColor * surfaceColour;
        lighting.AmbientColor = DayAmbientColor * surfaceColour;
        lighting.AmbientIntensity = DayAmbientIntensity;
        lighting.DiffuseIntensity = DayDiffuseIntensity;
        lighting.SpecularIntensity = DaySpecularIntensity;
        lighting.WorldSurfaceAmbientColour = surfaceColour;
        lighting.NightBlend = blend;
    }

    private static void ApplyCaveProfile(SceneLighting lighting)
    {
        // The existing cave presentation is independent of the outdoor clock.
        lighting.LightColor = new Vector3(0.46f, 0.60f, 1f);
        lighting.AmbientColor = new Vector3(0.83f, 0.86f, 1f);
        lighting.AmbientIntensity = 0.48f;
        lighting.DiffuseIntensity = 0.25f;
        lighting.SpecularIntensity = 0.40f;
        lighting.WorldSurfaceAmbientColour = new Vector3(0.48f, 0.49f, 0.52f);
        lighting.NightBlend = 1;
    }

    private void ApplyCelestialLighting(SceneLighting lighting)
    {
        // Sacred's billboard art and authored static-shadow atlas assume one fixed
        // projection. The lighting profile changes with time of day, while the sun
        // angles remain independently controlled by the debug settings.
        var solar = SolarLightingCalculator.Calculate(lighting.NightBlend);
        lighting.DirectionToLight = solar.DirectionToLight;
        lighting.DirectionToSun = solar.DirectionToSun;
        lighting.SunHeight = solar.SunHeight;
        lighting.OutdoorShadowOpacity = _debugDayFraction is null && _networkDayFraction is null && Mode == WorldLightingMode.PitchBlack ? 0.0f : solar.ShadowOpacity;
        lighting.ShadowOpacity = lighting.OutdoorShadowOpacity;
        lighting.ShadowMode = lighting.ShadowOpacity > 0.0f
            ? SceneShadowMode.Directional
            : SceneShadowMode.None;
    }

    private void ResetClock()
    {
        _debugDayFraction = null;
        // A freshly entered timed cycle starts at noon, matching the fixed Day profile.
        _cycleElapsedSeconds = Mode == WorldLightingMode.TimedDayNightCycle
            ? DayDurationSeconds * (720f - WorldDayNightLighting.DawnEnd) /
              (WorldDayNightLighting.DuskStart - WorldDayNightLighting.DawnEnd)
            : 0.0f;
    }

    private static void ApplyPitchBlack(SceneLighting lighting)
    {
        lighting.LightColor = Vector3.Zero;
        lighting.AmbientColor = Vector3.Zero;
        lighting.AmbientIntensity = 0;
        lighting.DiffuseIntensity = 0;
        lighting.SpecularIntensity = 0;
        lighting.WorldSurfaceAmbientColour = Vector3.Zero;
        lighting.NightBlend = 1;
        lighting.OutdoorShadowOpacity = 0;
        lighting.IndoorShadowOpacity = 0;
        lighting.ShadowOpacity = 0;
        lighting.ShadowMode = SceneShadowMode.None;
    }

    private enum LightingCyclePhase
    {
        Day,
        Dusk,
        Night,
        Dawn
    }
}
