using System.Runtime.InteropServices;

namespace Sacred.Core.World.Sector;

/// <summary>Shared byte layout of native cAnimalSpawn and cMonsterSpawn entries.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SectorSpawnLayout
{
    public const int SerializedSize = 8;
    /// <summary>Native type.</summary>
    [FieldOffset(0x00)] public readonly uint Type;
    /// <summary>Native quote. Preserve the authored value without assuming percentage units.</summary>
    [FieldOffset(0x04)] public readonly uint Quote;
}
