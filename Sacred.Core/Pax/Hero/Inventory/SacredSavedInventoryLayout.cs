namespace Sacred.Core.Pax.Hero.Inventory;

/// <summary>Gold file/network inventory stream, writer 0x5DDA30. All offsets precede the variable lists.</summary>
public static class SacredSavedInventoryLayout
{
    public const int InventoryIdOffset = 0;
    public const int FlagsOffset = 2;
    public const int Word04Offset = 4;
    public const int Word06Offset = 6;
    public const int WidthOffset = 8;
    public const int HeightOffset = 10;
    public const int KeysOffset = 12;
    public const int KeyCount = 256;
    public const int CellsOffset = 1036;
    public const int CellCount = 256;
    public const int CellSize = 12;
    public const int ItemBarOffset = 4108;
    public const int ItemBarCount = 10;
    public const int ItemBarRecordSize = 12;
    public const int SpellBarOffset = 4228;
    public const int SpellBarCount = 10;
    public const int SpellBarRecordSize = 20;
    public const int ActiveSlotOffset = 4428;
    public const int VampireActiveSlotOffset = 4430;
    // Gold getActiveSlot(type): type 0 is weapons, type 1 is combat arts.
    public const int SelectedWeaponSlotOffset = ActiveSlotOffset;
    public const int SelectedCombatArtSlotOffset = VampireActiveSlotOffset;
    public const int OwnerReferenceOffset = 4432;
    public const int QuestCountOffset = 4436;
    public const int QuestRecordsOffset = 4440;
    public const int QuestRecordSize = 8;
    public const int ScrollRecordSize = 4;
    public const int MinimumSize = 4444;
}
