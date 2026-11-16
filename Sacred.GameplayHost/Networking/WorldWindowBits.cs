namespace Sacred.GameplayHost.Networking;

/// <summary>Original 0x732550/0x7325C0. Captured native window +0x10 flags and
/// synchronous virtual +0x24 service, without OS window/input bindings.</summary>
public static class WorldWindowBits
{
    public static void Apply(WorldControlMemory window, uint mask, bool set, Action<uint> visibility)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(visibility);
        if (set && (window.ReadWord(0x10) & 1) == 0 && (mask & 1) != 0) visibility(1);
        if ((mask & 0x30) != 0)
        {
            Clear(0x20000);
            Clear(0x20000000);
        }
        var previous = window.ReadWord(0x10);
        var next = set ? previous | mask : previous & ~mask;
        window.WriteWord(0x10, next);
        if (!set && (previous & 1) != 0 && (next & 1) == 0) visibility(0);

        void Clear(uint bits)
        {
            var previous = window.ReadWord(0x10);
            var next = previous & ~bits;
            window.WriteWord(0x10, next);
            if ((previous & 1) != 0 && (next & 1) == 0) visibility(0);
        }
    }
}
