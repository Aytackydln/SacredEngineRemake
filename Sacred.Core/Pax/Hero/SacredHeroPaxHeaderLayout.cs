using System.Runtime.InteropServices;

namespace Sacred.Core.Pax.Hero;

/// <summary>Gold LoadHero 0x6058A0 reads a 256-byte AMH header before its chunk directory.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredHeroPaxHeaderLayout
{
    public const int Size = 256;
    // The native loader reserves sixteen directory entries on its stack.
    public const int DirectoryCapacity = 16;
    [FieldOffset(0)] public readonly byte MarkerA;
    [FieldOffset(1)] public readonly byte MarkerM;
    [FieldOffset(2)] public readonly byte MarkerH;
    /// <summary>Passed to the native object import method at 0x605A93..0x605AA7.</summary>
    [FieldOffset(3)] public readonly byte ObjectFormatVersion;
    [FieldOffset(4)] public readonly uint ChunkCount;
    [FieldOffset(8)] public readonly uint Word08;
}
