namespace Sacred.Core.World.Portals;

/// <summary>Gold cParticleSystem_stargate's native visual recipe (0x77FBE0..0x780C62).
/// These are executable constants, not guessed Texture.pak entry identifiers.</summary>
public static class SacredPortalAppearance
{
    /// <summary>Identifies the native portal recipes by their authored Items.pak effect labels.</summary>
    public static bool TryGetVariant(string effectName, out SacredPortalVariant variant)
    {
        if (effectName.Equals("FX_STARGATE", StringComparison.OrdinalIgnoreCase))
        {
            variant = SacredPortalVariant.Ancaria;
            return true;
        }
        if (effectName.Equals("FX_STARGATE_UW", StringComparison.OrdinalIgnoreCase))
        {
            variant = SacredPortalVariant.Underworld;
            return true;
        }
        variant = default;
        return false;
    }

    public const string FirstWhirlTexture = "FX_WHIRL01.TGA";
    public const string SecondWhirlTexture = "FX_WHIRL02.TGA";
    public const float CenterHeight = 95;
    public const float WhirlHalfWidth = 125;
    public const float WhirlHalfHeight = 220;
    public const float SurfaceWidth = 114; // (20 - 1) * 6
    public const float SurfaceHeight = 198; // (34 - 1) * 6
    public const float FirstWhirlOpacity = 143f / 255;
    public const float SecondWhirlOpacity = 175f / 255;

    public static IReadOnlyList<string> SurfaceTextures { get; } = Array.AsReadOnly(new[]
    {
        "MINIMAP058028.TGA", "MINIMAP063031.TGA", "MINIMAP049032.TGA", "MINIMAP034035.TGA",
        "MINIMAP061032.TGA", "MINIMAP044044.TGA", "MINIMAP044043.TGA", "MINIMAP044042.TGA",
        "MINIMAP044041.TGA", "MINIMAP045044.TGA", "MINIMAP050036.TGA", "MINIMAP050035.TGA",
        "MINIMAP050037.TGA", "MINIMAP051034.TGA", "MINIMAP051035.TGA", "MINIMAP051036.TGA"
    });

    // Gold's Underworld constructor (0x7C5C10) uses the same mesh and animation,
    // with this sixteen-slot texture table (including its intentional repetitions).
    public static IReadOnlyList<string> UnderworldSurfaceTextures { get; } = Array.AsReadOnly(new[]
    {
        "MINIMAP005005.TGA", "MINIMAP005026.TGA", "MINIMAP024018.TGA", "MINIMAP033009.TGA",
        "MINIMAP027022.TGA", "MINIMAP009015.TGA", "MINIMAP050070.TGA", "MINIMAP005005.TGA",
        "MINIMAP005026.TGA", "MINIMAP024018.TGA", "MINIMAP033009.TGA", "MINIMAP027022.TGA",
        "MINIMAP009015.TGA", "MINIMAP050070.TGA", "MINIMAP027022.TGA", "MINIMAP024018.TGA"
    });

    public static IReadOnlyList<string> GetSurfaceTextures(SacredPortalVariant variant) =>
        variant == SacredPortalVariant.Underworld ? UnderworldSurfaceTextures : SurfaceTextures;

    public static string GetFirstWhirlTexture(SacredPortalVariant variant) =>
        variant == SacredPortalVariant.Underworld ? "FX_WHIRL03.TGA" : FirstWhirlTexture;

    public static string GetSecondWhirlTexture(SacredPortalVariant variant) =>
        variant == SacredPortalVariant.Underworld ? "FX_WHIRL04.TGA" : SecondWhirlTexture;
}
