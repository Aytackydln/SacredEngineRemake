using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Pax.Hero.Inventory;

/// <summary>Gold inventory+0 vector, serialized at QuestRecordsOffset by 5DDA30/5DDB35.
/// 5D8280 rejects a new pair when an existing key matches or an existing Word04 is zero.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredSavedInventoryQuestRecordLayout
{
    public const int Size = 8;
    [FieldOffset(0)] public readonly uint KeyWord;
    [FieldOffset(4), BinaryUnknown] public readonly uint Word04;
}
