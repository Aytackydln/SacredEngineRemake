namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold CreateNPC 0x4831D8. Sector runtime +0x30 is initialized to zero
/// by 0x62373A and gains 0x10000 after tile-data initialization (0x637AAE).
/// The original KEYX flags are copied separately into native +0x2E.</summary>
public static class WorldScriptCreaturePlacementBranch
{
    public const uint TileDataInitializedFlag = 0x10000;
    public static bool UsesPositionedFactory(uint? sectorRuntimeFlags) =>
        sectorRuntimeFlags.HasValue && (sectorRuntimeFlags.Value & TileDataInitializedFlag) != 0;
}
