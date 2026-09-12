using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.World.Sector;

/// <summary>
/// Native cSectorEnvironment, embedded at KEYX +0x1E9. Gold 0x6384D8 copies
/// the same 0x100-byte block as the demo. Reserved regions remain uninterpreted.
/// </summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SectorEnvironmentLayout
{
    public const int SerializedSize = 0x100;

    [FieldOffset(0x00)] public readonly SectorEnvironmentSections Sections;
    [FieldOffset(0x04)] public readonly SectorEntryMask WeatherValid;
    [FieldOffset(0x05)] public readonly SectorWeatherEntries Weather;
    [FieldOffset(0x75)] public readonly SectorEntryMask AnimalsValid;
    [FieldOffset(0x76)] public readonly SectorSpawnEntries Animals;
    [FieldOffset(0x96)] public readonly ushort AnimalMinCount;
    [FieldOffset(0x98)] public readonly ushort AnimalMaxCount;
    [FieldOffset(0x9A), BinaryUnknown] public readonly WorldBytes20 Reserved9A;
    [FieldOffset(0xAE)] public readonly SectorEntryMask MonstersValid;
    [FieldOffset(0xAF)] public readonly SectorSpawnEntries Monsters;
    [FieldOffset(0xCF)] public readonly ushort MonsterMinCount;
    [FieldOffset(0xD1)] public readonly ushort MonsterMaxCount;
    [FieldOffset(0xD3)] public readonly ushort MonsterMinLevel;
    [FieldOffset(0xD5)] public readonly ushort MonsterMaxLevel;
    /// <summary>Native region01.</summary>
    [FieldOffset(0xD7)] public readonly byte Region;
    [FieldOffset(0xD8), BinaryUnknown] public readonly WorldBytes23 ReservedD8;
    [FieldOffset(0xEF)] public readonly uint MusicId;
    /// <summary>Native osSoundProfile.</summary>
    [FieldOffset(0xF3)] public readonly uint SoundProfileId;
    [FieldOffset(0xF7)] public readonly byte LiquidStyleA;
    [FieldOffset(0xF8)] public readonly byte LiquidStyleB;
    [FieldOffset(0xF9), BinaryUnknown] public readonly WorldBytes7 ReservedF9;
}
