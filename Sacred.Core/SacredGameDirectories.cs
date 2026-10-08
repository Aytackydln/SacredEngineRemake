namespace Sacred.Core;

public class SacredGameDirectories
{
    public required string GlobalResourcesPath { get; init; }

    /// <summary>
    /// Optional explicit path to Sacred's stairs-zone table. Engine clients infer
    /// <c>bin\treppe.bin</c> from the PAK directory when this is not supplied.
    /// </summary>
    public string? StairsMapPath { get; init; }

    /// <summary>
    /// Optional explicit path to the named arrival positions used to link two-way stairs.
    /// Engine clients use the selected campaign's <c>DefPos.bin</c> when omitted.
    /// </summary>
    public string? DefPosPath { get; init; }

    /// <summary>A bin subdirectory name or absolute script directory. Defaults to NetScriptCamp.</summary>
    public string? CampaignScriptsDirectory { get; init; }

    /// <summary>
    /// Optional explicit path to Sacred's item-set table. Engine clients infer
    /// <c>bin\sets.bin</c> from the PAK directory when this is not supplied.
    /// </summary>
    public string? ItemSetsPath { get; init; }
    /// <summary>Optional wpmod.bin path; inferred from the selected game's bin directory.</summary>
    public string? WeaponModifiersPath { get; init; }
    
    public required string ItemsPakPath { get; init; }
    public required string WeaponsPakPath { get; init; }
    public required string TexturesPakPath { get; init; }
}
