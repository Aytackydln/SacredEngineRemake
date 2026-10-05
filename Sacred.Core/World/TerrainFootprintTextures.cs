namespace Sacred.Core.World;

public static class TerrainFootprintTextures
{
    /// <summary>Original Texture.pak names; water frame numbering starts at one.</summary>
    public static string? FrameNameFormat(TerrainFootprintKind kind) => kind switch
    {
        TerrainFootprintKind.Desert => "FOOTSTEPDESERT.TGA",
        TerrainFootprintKind.Snow => "FOOTSTEPSNOW.TGA",
        TerrainFootprintKind.Swamp => "FOOTSTEPSWAMP.TGA",
        TerrainFootprintKind.Water => "footstepWaterA_{0:00}.tga",
        _ => null
    };

    public static int FrameCount(TerrainFootprintKind kind) => kind == TerrainFootprintKind.Water ? 5 : 1;
}
