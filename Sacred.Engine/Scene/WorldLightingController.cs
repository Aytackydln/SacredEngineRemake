using System;
using System.Numerics;

namespace Sacred.Engine.Scene;

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
    private const float DayDurationSeconds = 15.0f;
    private const float NightDurationSeconds = 10.0f;
    private const float TransitionDurationSeconds = 5.0f;

    private const float CycleDurationSeconds =
        DayDurationSeconds + NightDurationSeconds + TransitionDurationSeconds * 2.0f;

    private float _cycleElapsedSeconds;

    public WorldLightingMode Mode { get; private set; } = WorldLightingMode.TimedDayNightCycle;

    public void CycleMode()
    {
        Mode = Mode switch
        {
            WorldLightingMode.Day => WorldLightingMode.Night,
            WorldLightingMode.Night => WorldLightingMode.PitchBlack,
            WorldLightingMode.TimedDayNightCycle => WorldLightingMode.Night,
            WorldLightingMode.PitchBlack => WorldLightingMode.Day,
            _ => WorldLightingMode.Day
        };
        _cycleElapsedSeconds = 0.0f;
    }

    /// <returns><see langword="true"/> when a timed cycle enters a new lighting phase.</returns>
    public bool Update(float elapsedSeconds, SceneLighting lighting)
    {
        var previousPhase = GetCyclePhase();
        if (Mode == WorldLightingMode.TimedDayNightCycle)
        {
            _cycleElapsedSeconds = (_cycleElapsedSeconds + MathF.Max(0.0f, elapsedSeconds)) %
                                   CycleDurationSeconds;
        }
        else if (Mode == WorldLightingMode.PitchBlack)
        {
            ApplyPitchBlack(lighting);
            return false;
        }

        ApplyProfile(GetNightBlend(), lighting);
        return Mode == WorldLightingMode.TimedDayNightCycle && previousPhase != GetCyclePhase();
    }

    public string DisplayName => Mode switch
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
        lighting.AmbientIntensity = Lerp(0.28f, 0.18f, blend);
        lighting.DiffuseIntensity = Lerp(0.85f, 0.30f, blend);
        lighting.SpecularIntensity = Lerp(0.20f, 0.08f, blend);
        lighting.WorldQuadAmbientIntensity = Lerp(1.0f, 0.30f, blend);
    }

    private static void ApplyPitchBlack(SceneLighting lighting)
    {
        lighting.LightColor = Vector3.Zero;
        lighting.AmbientColor = Vector3.Zero;
        lighting.AmbientIntensity = 0;
        lighting.DiffuseIntensity = 0;
        lighting.SpecularIntensity = 0;
        lighting.WorldQuadAmbientIntensity = 0;
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
