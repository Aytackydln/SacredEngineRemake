namespace Sacred.Core.World.Sector;

/// <summary>Validity bits selecting entries in a four-element environment/spawn table.</summary>
[Flags]
public enum SectorEntryMask : byte
{
    None = 0,
    Entry0 = 1,
    Entry1 = 2,
    Entry2 = 4,
    Entry3 = 8,
}
