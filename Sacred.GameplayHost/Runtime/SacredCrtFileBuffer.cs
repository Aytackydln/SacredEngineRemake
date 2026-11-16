using Sacred.Core.Executable;

namespace Sacred.GameplayHost.Runtime;

public static class SacredCrtFileBuffer
{
    public static void Allocate(SacredCrtFileSlot file, SacredCrtBufferDefinition definition, Func<int, bool> allocate)
    {
        if (allocate(checked((int)definition.AllocationSize)))
        {
            file.Flags |= 8; file.BufferSize = definition.BufferSize; file.Storage = SacredCrtBufferStorage.Heap;
        }
        else
        {
            file.Flags |= 4; file.BufferSize = definition.FallbackSize; file.Storage = SacredCrtBufferStorage.Inline;
        }
    }
    public static void Free(SacredCrtFileSlot file, Action free)
    {
        if ((file.Flags & 0x83) == 0 || (file.Flags & 8) == 0) return;
        free(); file.Flags &= 0xfffffbf7; file.Storage = SacredCrtBufferStorage.None;
    }
}
