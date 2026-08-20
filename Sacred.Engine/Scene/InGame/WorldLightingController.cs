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
    private const float SunriseTime = 0.25f;
    private const float NoonTime = 0.50f;
    private const float SunsetTime = 0.75f;
    private const float DayDurationSeconds = 15.0f;
    private const float NightDurationSeconds = 10.0f;
    private const float TransitionDurationSeconds = 5.0f;
    private const float IndoorContactShadowOpacity = 0.32f;

    private const float CycleDurationSeconds =
        DayDurationSeconds + NightDurationSeconds + TransitionDurationSeconds * 2.0f;
    private const float DuskEndSeconds = DayDurationSeconds + TransitionDurationSeconds;
    private const float DawnStartSeconds = DuskEndSeconds + NightDurationSeconds;
    private const float DawnToDuskDurationSeconds =
        TransitionDurationSeconds + DayDurationSeconds + TransitionDurationSeconds;

    private float _cycleElapsedSeconds;
    private bool _zoneInitialized;

    public WorldLightingController(WorldLightingMode mode = WorldLightingMode.TimedDayNightCycle)
    {
        Mode = mode;
        ResetClock();
    }

    public WorldLightingMode Mode { get; private set; }
    public WorldZone CurrentZone { get; private set; } = WorldZone.Outdoors;

    public void CycleMode()
    {
        Mode = Mode switch
        {
            WorldLightingMode.Day => WorldLightingMode.Night,
            WorldLightingMode.Night => WorldLightingMode.PitchBlack,
            WorldLightingMode.TimedDayNightCycle => WorldLightingMode.Night,
            WorldLightingMode.PitchBlack => WorldLightingMode.TimedDayNightCycle,
            _ => WorldLightingMode.Day
        };
        ResetClock();
    }

    public void SetMode(WorldLightingMode mode)
    {
        Mode = mode;
        ResetClock();
    }

    /// <returns><see langword="true"/> when a timed cycle enters a new lighting phase.</returns>
    public bool Update(float elapsedSeconds, SceneLighting lighting, Vector3 focusPosition) =>
        Update(elapsedSeconds, lighting, focusPosition, WorldZone.Outdoors);

    /// <returns><see langword="true"/> when the visible lighting description changes.</returns>
    public bool Update(
        float elapsedSeconds,
        SceneLighting lighting,
        Vector3 focusPosition,
        WorldZone zone)
    {
        var previousPhase = GetCyclePhase();
        var zoneChanged = !_zoneInitialized || CurrentZone != zone;
        CurrentZone = zone;
        _zoneInitialized = true;
        if (Mode == WorldLightingMode.TimedDayNightCycle)
        {
            _cycleElapsedSeconds = (_cycleElapsedSeconds + MathF.Max(0.0f, elapsedSeconds)) %
                                   CycleDurationSeconds;
        }

        if (zone == WorldZone.Cave)
        {
            ApplyProfile(1.0f, lighting);
            ApplyCelestialLighting(lighting, focusPosition, dayTime: 0.0f);
            lighting.ShadowMode = SceneShadowMode.None;
            lighting.ShadowOpacity = 0.0f;
        }
        else if (Mode == WorldLightingMode.PitchBlack)
        {
            ApplyPitchBlack(lighting);
            ApplyCelestialLighting(lighting, focusPosition);
        }
        else
        {
            ApplyProfile(GetNightBlend(), lighting);
            ApplyCelestialLighting(lighting, focusPosition);
            if (zone == WorldZone.Indoors)
            {
                lighting.ShadowMode = SceneShadowMode.SoftContact;
                lighting.ShadowOpacity = IndoorContactShadowOpacity;
            }
        }

        if (zoneChanged)
            EngineLog.WriteLine($"Lighting zone: {zone}");

        return zoneChanged ||
               Mode == WorldLightingMode.TimedDayNightCycle && previousPhase != GetCyclePhase();
    }

    public string DisplayName => CurrentZone switch
    {
        WorldZone.Cave => "Cave: Night",
        WorldZone.Indoors => $"Indoors: {ModeDisplayName}",
        _ => ModeDisplayName,
    };

    private string ModeDisplayName => Mode switch
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

    private float GetNightBlend() => Mode switch
    {
        WorldLightingMode.Day => 0.0f,
        WorldLightingMode.Night => 1.0f,
        _ => GetTimedNightBlend()
    };

    private float GetTimedNightBlend()
    {
        var phase = GetCyclePhase();
        return phase switch
        {
            LightingCyclePhase.Day => 0.0f,
            LightingCyclePhase.Dusk => SmoothStep((_cycleElapsedSeconds - DayDurationSeconds) /
                                                  TransitionDurationSeconds),
            LightingCyclePhase.Night => 1.0f,
            _ => 1.0f - SmoothStep(
                (_cycleElapsedSeconds - DayDurationSeconds - TransitionDurationSeconds - NightDurationSeconds) /
                TransitionDurationSeconds)
        };
    }

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

    private static void ApplyProfile(float nightBlend, SceneLighting lighting)
    {
        var blend = Math.Clamp(nightBlend, 0.0f, 1.0f);
        lighting.LightColor = Vector3.Lerp(new Vector3(1.0f, 0.93f, 0.82f), new Vector3(0.43f, 0.56f, 0.90f), blend);
        lighting.AmbientColor = Vector3.Lerp(new Vector3(0.76f, 0.84f, 1.0f), new Vector3(0.24f, 0.33f, 0.56f), blend);
        lighting.AmbientIntensity = Lerp(0.34f, 0.18f, blend);
        lighting.DiffuseIntensity = Lerp(0.82f, 0.12f, blend);
        // Moonlight keeps a cool diffuse response, but never contributes a specular lobe.
        lighting.SpecularIntensity = Lerp(0.16f, 0.0f, blend);
        lighting.WorldQuadAmbientIntensity = Lerp(1.0f, 0.30f, blend);
        lighting.UnlitStaticSpriteWhiteNits = SceneLighting.DefaultUnlitStaticSpriteWhiteNits;
        lighting.NightBlend = blend;
    }

    private void ApplyCelestialLighting(
        SceneLighting lighting,
        Vector3 focusPosition,
        float? dayTime = null)
    {
        var solar = SolarLightingCalculator.Calculate(
            dayTime ?? GetCelestialTime(),
            lighting.NightBlend,
            focusPosition);
        lighting.LightPosition = solar.LightPosition;
        lighting.DirectionToLight = solar.DirectionToLight;
        lighting.DirectionToSun = solar.DirectionToSun;
        lighting.SunHeight = solar.SunHeight;
        lighting.ShadowOpacity = Mode == WorldLightingMode.PitchBlack ? 0.0f : solar.ShadowOpacity;
        lighting.ShadowMode = lighting.ShadowOpacity > 0.0f
            ? SceneShadowMode.Directional
            : SceneShadowMode.None;
    }

    private float GetCelestialTime()
    {
        if (Mode == WorldLightingMode.Day)
            return NoonTime;
        if (Mode is WorldLightingMode.Night or WorldLightingMode.PitchBlack)
            return 0.0f;

        if (GetCyclePhase() == LightingCyclePhase.Night)
        {
            // Keep moon motion continuous on the complementary half of the same arc.
            var nightProgress = (_cycleElapsedSeconds - DuskEndSeconds) / NightDurationSeconds;
            var moonTime = SunsetTime + nightProgress * (1.0f - SunsetTime + SunriseTime);
            return moonTime >= 1.0f ? moonTime - 1.0f : moonTime;
        }

        // Interpolate time (therefore solar angle), not XYZ coordinates. Constant angular
        // velocity produces a naturally curved path and reaches noon halfway through the
        // complete dawn-to-dusk interval.
        var elapsedSinceDawn = _cycleElapsedSeconds - DawnStartSeconds;
        if (elapsedSinceDawn < 0.0f)
            elapsedSinceDawn += CycleDurationSeconds;
        var daylightProgress = Math.Clamp(
            elapsedSinceDawn / DawnToDuskDurationSeconds,
            0.0f,
            1.0f);
        return Lerp(SunriseTime, SunsetTime, daylightProgress);
    }

    private void ResetClock()
    {
        // A freshly entered timed cycle starts at noon, matching the fixed Day profile.
        _cycleElapsedSeconds = Mode == WorldLightingMode.TimedDayNightCycle
            ? DayDurationSeconds * 0.5f
            : 0.0f;
    }

    private static void ApplyPitchBlack(SceneLighting lighting)
    {
        lighting.LightColor = Vector3.Zero;
        lighting.AmbientColor = Vector3.Zero;
        lighting.AmbientIntensity = 0;
        lighting.DiffuseIntensity = 0;
        lighting.SpecularIntensity = 0;
        lighting.WorldQuadAmbientIntensity = 0;
        lighting.UnlitStaticSpriteWhiteNits = SceneLighting.DefaultUnlitStaticSpriteWhiteNits;
        lighting.NightBlend = 1;
        lighting.ShadowOpacity = 0;
        lighting.ShadowMode = SceneShadowMode.None;
    }

    private static float SmoothStep(float value)
    {
        var clamped = Math.Clamp(value, 0.0f, 1.0f);
        return clamped * clamped * (3.0f - 2.0f * clamped);
    }

    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;

    private enum LightingCyclePhase
    {
        Day,
        Dusk,
        Night,
        Dawn
    }
}
