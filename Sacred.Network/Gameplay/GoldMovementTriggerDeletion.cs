using System.Buffers.Binary;
using System.Text;
using Sacred.Core.World;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable movement-trigger deletion instruction; actual sector trigger-bank binding remains a world responsibility.</summary>
public sealed class GoldMovementTriggerDeletion
{
    private readonly byte[] _payload;
    private GoldMovementTriggerDeletion(byte[] payload) => _payload = payload;
    public uint Sector => BinaryPrimitives.ReadUInt32LittleEndian(_payload);
    public int FirstX => ReadInt(GoldMovementTriggerDeletionLayout.FirstXOffset);
    public int FirstY => ReadInt(GoldMovementTriggerDeletionLayout.FirstYOffset);
    public int SecondX => ReadInt(GoldMovementTriggerDeletionLayout.SecondXOffset);
    public int SecondY => ReadInt(GoldMovementTriggerDeletionLayout.SecondYOffset);
    public bool AppliesToAllSectors => FirstX < 0;
    public ReadOnlySpan<byte> NameBytes => _payload.AsSpan(GoldMovementTriggerDeletionLayout.NameOffset,
        _payload.Length - GoldMovementTriggerDeletionLayout.MinimumSize);
    /// <summary>Latin-1 diagnostic view; wire bytes retain the identity and encoding.</summary>
    public string Name => Encoding.Latin1.GetString(NameBytes);

    public static GoldMovementTriggerDeletion FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < GoldMovementTriggerDeletionLayout.MinimumSize ||
            payload[GoldMovementTriggerDeletionLayout.NameOffset..].IndexOf((byte)0) != payload.Length - GoldMovementTriggerDeletionLayout.MinimumSize)
            throw new InvalidDataException("Gold movement-trigger deletion requires five words and one complete NUL-terminated name.");
        return new(payload.ToArray());
    }

    public static GoldMovementTriggerDeletion Create(uint sector, int firstX, int firstY, int secondX, int secondY, ReadOnlySpan<byte> name)
    {
        if (name.IndexOf((byte)0) >= 0) throw new ArgumentException("Supply name bytes without the NUL terminator.", nameof(name));
        if (name.Length > GoldMessageCodec.MaximumMessageBytes - GoldMessageCodec.HeaderBytes - GoldMovementTriggerDeletionLayout.MinimumSize)
            throw new ArgumentOutOfRangeException(nameof(name));
        var payload = new byte[GoldMovementTriggerDeletionLayout.MinimumSize + name.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, sector);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), firstX);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8), firstY);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(12), secondX);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), secondY);
        name.CopyTo(payload.AsSpan(GoldMovementTriggerDeletionLayout.NameOffset));
        return new(payload);
    }

    /// <summary>Native scope: all table entries for negative first X; otherwise center then eight KEYX neighbors in authored order.</summary>
    public IReadOnlyList<uint> GetTargetSectors(SacredSectorIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (AppliesToAllSectors) return Array.AsReadOnly(Enumerable.Range(0, (int)GoldMovementTriggerDeletionLayout.MaximumSector + 1).Select(i => (uint)i).ToArray());
        if (Sector > GoldMovementTriggerDeletionLayout.MaximumSector) return Array.Empty<uint>();
        var result = new List<uint>(9) { Sector };
        if (index.TryGetSector(Sector, out var record))
            for (var i = 0; i < 8; i++)
                if (record.Neighbors[i] != 0 && record.Neighbors[i] <= GoldMovementTriggerDeletionLayout.MaximumSector)
                    result.Add(record.Neighbors[i]);
        return result.AsReadOnly();
    }

    /// <summary>Original default-locale name comparison: ASCII case folding; high bytes are unchanged.</summary>
    public bool MatchesName(ReadOnlySpan<byte> candidate)
    {
        var end = candidate.IndexOf((byte)0);
        if (end >= 0) candidate = candidate[..end];
        if (candidate.Length != NameBytes.Length) return false;
        for (var i = 0; i < candidate.Length; i++)
            if (Fold(candidate[i]) != Fold(NameBytes[i])) return false;
        return true;
    }

    public byte[] ToPayload() => _payload.ToArray();
    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(offset));
    private static byte Fold(byte value) => value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;
}
