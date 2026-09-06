using System;
using System.Numerics;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Builds the fixed, user-adjustable celestial direction and shadow data.</summary>
internal static class SolarLightingCalculator
{
    private const float CelestialDistance = 2000.0f;
    private const float MaximumShadowOpacity = 0.5f;

    // These reproduce the previous fixed 0.45 daytime direction. They are static so
    // debug controls can tune the authored shadow projection without coupling it to time of day.
    public const float DefaultSunAzimuthDegrees = -125f;
    public const float DefaultSunElevationDegrees = 60f;
    public static float SunAzimuthDegrees = DefaultSunAzimuthDegrees;
    public static float SunElevationDegrees = DefaultSunElevationDegrees;

    public static SolarLighting Calculate(float nightBlend, Vector3 focusPosition)
    {
        var azimuth = SunAzimuthDegrees * MathF.PI / 180.0f;
        var elevation = SunElevationDegrees * MathF.PI / 180.0f;
        var horizontalScale = MathF.Cos(elevation);
        var sunDirection = new Vector3(
            MathF.Cos(azimuth) * horizontalScale,
            MathF.Sin(azimuth) * horizontalScale,
            MathF.Sin(elevation));
        var sunAboveHorizon = MathF.Max(0.0f, sunDirection.Z);
        var daylight = 1.0f - Math.Clamp(nightBlend, 0.0f, 1.0f);

        return new SolarLighting(
            focusPosition + sunDirection * CelestialDistance,
            sunDirection,
            sunDirection,
            sunAboveHorizon,
            MaximumShadowOpacity * daylight);
    }
}

internal readonly record struct SolarLighting(
    Vector3 LightPosition,
    Vector3 DirectionToLight,
    Vector3 DirectionToSun,
    float SunHeight,
    float ShadowOpacity);
