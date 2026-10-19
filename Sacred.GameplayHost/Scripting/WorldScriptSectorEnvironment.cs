using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.World;
using Sacred.Core.World.Sector;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Session-owned mutable copy of the original KEYX environment. Script effects never
/// modify the archive or discard bytes whose meaning is still unknown.</summary>
public sealed class WorldScriptSectorEnvironment
{
    private readonly byte[] _bytes = new byte[SectorEnvironmentLayout.SerializedSize];
    public WorldScriptSectorEnvironment(KeyxSectorRecord sector)
    {
        Sector = checked((ushort)sector.Id);
        Coordinate = SacredSectorCoordinates.ToGrid(sector);
        var environment = sector.Environment;
        MemoryMarshal.Write(_bytes, in environment);
    }
    public ushort Sector { get; }
    public SectorCoord Coordinate { get; }
    public SectorEnvironmentLayout Value => MemoryMarshal.Read<SectorEnvironmentLayout>(_bytes);
    public byte[] ToPayload() => _bytes.ToArray();
    internal void SetPopulationTimestamp(uint milliseconds) => BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(0x9A), milliseconds);
    internal void SetMonsterParameters(int count, int minimumLevel, int maximumLevel)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(0xD1), unchecked((ushort)count));
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(0xD3), unchecked((ushort)minimumLevel));
        BinaryPrimitives.WriteUInt16LittleEndian(_bytes.AsSpan(0xD5), unchecked((ushort)maximumLevel));
    }
}
