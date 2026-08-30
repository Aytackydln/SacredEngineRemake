using System;
using System.Numerics;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Converts Sacred map time into sun, moon, and shadow directions.</summary>
internal static class SolarLightingCalculator
{
    private const float CelestialDistance = 2000.0f;
    private const float MaximumShadowOpacity = 0.5f;
    private const float MaximumSolarElevationRadians = 52.0f * MathF.PI / 180.0f;
    private const float NorthwardBias = 0.55f;

    // In the unrotated isometric map, screen-up is decreasing X and Y. This makes
    // north explicit and keeps the east-to-west solar path tied to map directions.
    private static readonly Vector2 North = Vector2.Normalize(new Vector2(-1.0f, -1.0f));
    private static readonly Vector2 East = Vector2.Normalize(new Vector2(1.0f, -1.0f));

    public static SolarLighting Calculate(float dayTime, float nightBlend, Vector3 focusPosition)
    {
        var time = dayTime - MathF.Floor(dayTime);
        var hourAngle = (time - 0.5f) * MathF.Tau;
        var solarHeight = MathF.Cos(hourAngle);
        var elevation = solarHeight * MaximumSolarElevationRadians;
        var horizontalScale = MathF.Cos(elevation);
        var verticalScale = MathF.Sin(elevation);
        var horizontalDirection =
            East * -MathF.Sin(hourAngle) +
            North * (NorthwardBias + MathF.Max(0.0f, solarHeight));
        horizontalDirection = horizontalDirection.LengthSquared() > float.Epsilon
            ? Vector2.Normalize(horizontalDirection)
            : North;

        var sunDirection = Vector3.Normalize(new Vector3(
            horizontalDirection.X * horizontalScale,
            horizontalDirection.Y * horizontalScale,
            verticalScale));
        var sunAboveHorizon = MathF.Max(0.0f, solarHeight);
        var directionToLight = solarHeight >= 0.0f ? sunDirection : -sunDirection;
        var daylight = 1.0f - Math.Clamp(nightBlend, 0.0f, 1.0f);

        return new SolarLighting(
            focusPosition + directionToLight * CelestialDistance,
            directionToLight,
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
