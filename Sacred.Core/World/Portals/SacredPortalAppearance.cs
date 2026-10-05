namespace Sacred.Core.World.Portals;

/// <summary>Gold cParticleSystem_stargate's native visual recipe (0x77FBE0..0x780C62).
/// These are executable constants, not guessed Texture.pak entry identifiers.</summary>
public static class SacredPortalAppearance
{
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
}
