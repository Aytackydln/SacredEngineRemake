using Sacred.Core.World;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared 0x637AF0 dormant ordinary ground QuestItem re-placement.
/// Physical re-insertion preserves item/base values and never registers a
/// non-creature in the creature sector vector. The caller commits the tile head.</summary>
public static class WorldScriptQuestItemSectorReload
{
    public static uint Reinsert(WorldEquipmentInitialItem item, WldxTileRecord tile, uint currentHead)
    {
        if (item.Node.Active || item.Physical.AttachmentParent != 0)
            throw new NotSupportedException("Sector reload requires the compared dormant ground QuestItem and ordinary tile.");
        // The native detach discards the old next pointer; insertion ignores a self head.
        item.NextOnTile = WorldScriptGroundObjectSectorReload.Next(item.Physical.Base,tile,currentHead);
        return item.Physical.Base.ObjectReference;
    }
}
