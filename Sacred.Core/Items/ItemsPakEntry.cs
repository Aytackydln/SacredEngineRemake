namespace Sacred.Core.Items;

public readonly record struct ItemsPakEntry(
    ItemsPakEntryInfo EntryInfo,
    ItemsPakEntryModelDesc ModelDesc
);
