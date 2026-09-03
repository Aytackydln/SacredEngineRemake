using System;
using System.Numerics;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Converts Sacred map time into sun, moon, and shadow directions.</summary>
internal static class SolarLightingCalculator
{
    private const float CelestialDistance = 2000.0f;
    private const float MaximumShadowOpacity = 0.5f;
    private const float MaximumSolarElevationRadians = 58.0f * MathF.PI / 180.0f;
    private const float NoonAxisBias = 0.55f;

    // Sacred's sun is south-west of the scene. Native Y must be inverted when
    // expressed in the remake's world coordinates; elevation is calibrated above
    // from the original game's shadow length at the reference map position.
    private static readonly Vector2 NoonHorizontalDirection =
        Vector2.Normalize(new Vector2(-0.85f, -1.0f));
    private static readonly Vector2 PerpendicularHorizontalDirection =
        Vector2.Normalize(new Vector2(1.0f, -1.0f));

    public static SolarLighting Calculate(float dayTime, float nightBlend, Vector3 focusPosition)
    {
        var time = dayTime - MathF.Floor(dayTime);
        var hourAngle = (time - 0.5f) * MathF.Tau;
        var solarHeight = MathF.Cos(hourAngle);
        var elevation = solarHeight * MaximumSolarElevationRadians;
        var horizontalScale = MathF.Cos(elevation);
        var verticalScale = MathF.Sin(elevation);
        var horizontalDirection =
            PerpendicularHorizontalDirection * -MathF.Sin(hourAngle) +
            NoonHorizontalDirection * (NoonAxisBias + MathF.Max(0.0f, solarHeight));
        horizontalDirection = horizontalDirection.LengthSquared() > float.Epsilon
            ? Vector2.Normalize(horizontalDirection)
            : NoonHorizontalDirection;

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
