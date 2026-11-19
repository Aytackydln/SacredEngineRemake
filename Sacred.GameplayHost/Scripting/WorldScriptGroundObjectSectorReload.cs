using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>0x637AF0's compared ground QuestItem/Container physical re-placement.
/// The caller walks references in order and commits the returned chain successor.
/// Other flags, elevated surfaces and special terrain retain explicit boundaries.</summary>
public static class WorldScriptGroundObjectSectorReload
{
    public static uint Next(GoldObjectBaseState basis, WldxTileRecord tile, uint currentHead)
    {
        if ((basis.Flags & ~0x10000u) != 0x201 || basis.Surface != 0 || tile.Properties.Behavior != WldxTileBehavior.None)
            throw new NotSupportedException("Sector reload requires the compared ordinary placed ground object and tile.");
        return currentHead != basis.ObjectReference ? currentHead : 0;
    }
}
