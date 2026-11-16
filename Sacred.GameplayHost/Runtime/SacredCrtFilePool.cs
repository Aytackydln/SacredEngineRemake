namespace Sacred.GameplayHost.Runtime;

public sealed class SacredCrtFilePool(IList<SacredCrtFileSlot?> slots)
{
    public SacredCrtFileSlot? Acquire(Func<int, bool> allocate)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            if (slot is not null && (slot.Flags & 0x83) != 0) continue;
            if (slot is null)
            {
                if (!allocate(0x38)) return null;
                slots[index] = slot = new();
            }
            slot.Reset(); return slot;
        }
        return null;
    }
}
