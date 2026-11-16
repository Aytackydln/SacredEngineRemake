namespace Sacred.Core.Runtime;

/// <summary>Original 64-block, 32-descriptor allocation search. Initial tables
/// are explicit CRT startup inputs, not inferred standard-handle defaults.</summary>
public sealed class SacredCrtDescriptorPool(IList<SacredCrtDescriptor[]?> blocks)
{
    public (int Index, SacredCrtDescriptor Slot)? Acquire(Func<int, bool> allocate)
    {
        for (var blockIndex = 0; blockIndex < 64; blockIndex++)
        {
            var block = blockIndex < blocks.Count ? blocks[blockIndex] : null;
            if (block is null)
            {
                if (!allocate(0x480)) return null;
                block = Enumerable.Range(0, 32).Select(_ => new SacredCrtDescriptor { Handle = uint.MaxValue, Lookahead = 10 }).ToArray();
                while (blocks.Count <= blockIndex) blocks.Add(null);
                blocks[blockIndex] = block;
            }
            for (var index = 0; index < block.Length; index++)
                if ((block[index].Flags & 1) == 0)
                {
                    block[index].Handle = uint.MaxValue;
                    return (blockIndex * 32 + index, block[index]);
                }
        }
        return null;
    }
    public IEnumerable<(int Index, SacredCrtDescriptor Slot)> OpenDescriptors() => blocks
        .SelectMany((block, blockIndex) => (block ?? []).Select((slot, index) => (Index: blockIndex * 32 + index, Slot: slot)))
        .Where(item => (item.Slot.Flags & 1) != 0);
}
