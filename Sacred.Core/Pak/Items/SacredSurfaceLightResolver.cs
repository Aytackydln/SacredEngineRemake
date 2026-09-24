using System.Collections.Frozen;

namespace Sacred.Core.Pak.Items;

/// <summary>
/// Resolves world-surface illumination from an Items.pak entry.
/// Invisible SimpleLight entries are completely described by the descriptor.
/// The visible fixtures have no shared game-file discriminator, so their
/// attributes are the measured source table from surface-lighting-samples.
/// </summary>
public static class SacredSurfaceLightResolver
{
    private const ushort PlayerFallbackRadius = 300;

    private static readonly SacredSurfaceLightAttributes DefaultMarkerAttributes = new(
        Radius: 0,
        Opacity: 0.48f,
        Colour: SacredSurfaceLightColour.White,
        Anchor: SacredSurfaceLightAnchor.GroundAnchor);

    /// <summary>
    /// Player-held illumination uses the largest authored marker radius. Some
    /// archive variants omit those invisible marker records, so retain Sacred's
    /// small-light reach as the player source's explicit fallback.
    /// </summary>
    public static SacredSurfaceLightAttributes CreatePlayerAttributes(ushort radius) => new(
        Radius: radius == 0 ? PlayerFallbackRadius : radius,
        Opacity: 0.48f,
        Colour: SacredSurfaceLightColour.White,
        Anchor: SacredSurfaceLightAnchor.GroundAnchor);

    private static readonly FrozenDictionary<string, SacredSurfaceLightAttributes> VisibleFixtureAttributes =
        new Dictionary<string, SacredSurfaceLightAttributes>(StringComparer.Ordinal)
        {
            // Circle diameters in the annotated captures, converted with their
            // fitted world-to-screen projection. Keep these names here until a
            // game-file field can distinguish visible surface-light fixtures.
            ["Candle 5"] = Warm(radius: 440),
            ["Candle 6"] = Warm(radius: 440),
            ["Candelabra 1"] = Warm(radius: 600),
            ["DUN_light_1"] = Cold(radius: 600),
            ["DUN_torch2"] = Warm(radius: 440),
            ["CandleWall01"] = Warm(radius: 440),
            ["3S_FIRE"] = Warm(radius: 440),
            ["FEUERSTELLE_MIT_STEIN_01"] = Warm(radius: 360),
            ["LICHTER_HAENGEND_KLEIN"] = Cold(radius: 600),
            ["LICHTER_HAENGEND_KLEIN_1"] = Cold(radius: 600),
            ["LICHTER_HAENGEND_MITTEL"] = Cold(radius: 600),
            ["LICHTER_KLEIN"] = Cold(radius: 600),
            ["LICHTER_MITTEL"] = Cold(radius: 600)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static bool TryResolve(ItemsPakEntry item, out SacredSurfaceLight light)
    {
        if (item.ModelDesc.IsWorldLightMarker)
        {
            light = new SacredSurfaceLight(
                DefaultMarkerAttributes with { Radius = item.ModelDesc.Radius },
                SacredSurfaceLightKind.Marker);
            return light.Attributes.Radius > 0;
        }

        if (VisibleFixtureAttributes.TryGetValue(item.ModelName, out var attributes))
        {
            light = new SacredSurfaceLight(attributes, SacredSurfaceLightKind.VisibleFixture);
            return true;
        }

        light = default;
        return false;
    }

    private static SacredSurfaceLightAttributes Warm(ushort radius) => new(
        radius,
        Opacity: 0.48f,
        Colour: new SacredSurfaceLightColour(255, 198, 112),
        Anchor: SacredSurfaceLightAnchor.SpriteCenter);

    private static SacredSurfaceLightAttributes Cold(ushort radius) => new(
        radius,
        Opacity: 0.48f,
        Colour: new SacredSurfaceLightColour(126, 198, 255),
        Anchor: SacredSurfaceLightAnchor.SpriteCenter);
}

/// <summary>Surface-light parameters associated with one item-model name.</summary>
public readonly record struct SacredSurfaceLightAttributes(
    ushort Radius,
    float Opacity,
    SacredSurfaceLightColour Colour,
    SacredSurfaceLightAnchor Anchor);

public readonly record struct SacredSurfaceLight(
    SacredSurfaceLightAttributes Attributes,
    SacredSurfaceLightKind Kind);

public readonly record struct SacredSurfaceLightColour(byte Red, byte Green, byte Blue)
{
    public static SacredSurfaceLightColour White { get; } = new(255, 255, 255);
}

/// <summary>Coordinate space used to centre the source's illumination circle.</summary>
public enum SacredSurfaceLightAnchor : byte
{
    GroundAnchor,
    SpriteCenter
}

public enum SacredSurfaceLightKind : byte
{
    Marker,
    VisibleFixture
}
