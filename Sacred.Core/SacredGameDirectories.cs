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
    /// Engine clients infer <c>bin\NetScript\DefPos.bin</c> when omitted.
    /// </summary>
    public string? DefPosPath { get; init; }
    
    public required string ItemsPakPath { get; init; }
    public required string WeaponsPakPath { get; init; }
    public required string TexturesPakPath { get; init; }
}
