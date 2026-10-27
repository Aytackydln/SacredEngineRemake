using System.Collections.Generic;
using Sacred.Inventory.Items;

namespace Sacred.UI.Inventory;

/// <summary>Native 32-pixel socket cells, drawn at a 40-pixel pitch by 0x5C0EE0.</summary>
public sealed record ItemDescriptionSocket(int Index, byte Kind, uint ItemType)
{
    public const int Size = 32, Pitch = 40;
    public uint Color => Kind switch
    {
        1 => 0xFFB14910, 2 => 0xFFA1A1A1, 3 => 0xFFD8AB30, 4 => 0xFF009F00, _ => 0xFFFFFFFF
    };

    public static IReadOnlyList<ItemDescriptionSocket> Create(SacredItemInstance item)
    {
        var kinds = item.SlotTypes;
        var count = 0;
        foreach (var kind in kinds) if (kind != 0) count++;
        var cells = new List<ItemDescriptionSocket>(count);
        // Original rendering iterates the counted cells in stored order, including authored gaps.
        for (var index = 0; index < count; index++)
            cells.Add(new(index, kinds[index], index < item.SocketRecords.Count ? item.SocketRecords[index].ItemType : 0));
        return cells;
    }
}
