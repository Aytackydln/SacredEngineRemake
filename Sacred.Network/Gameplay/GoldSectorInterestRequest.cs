using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Immutable sector interest request, including its reserved bytes and available reference count.</summary>
public sealed class GoldSectorInterestRequest
{
    private readonly byte[] _payload;
    private GoldSectorInterestRequest(byte[] payload) => _payload = payload;
    public ushort CurrentSector => Word(GoldSectorInterestLayout.CurrentSectorOffset);
    public ushort PreviousSector => Word(GoldSectorInterestLayout.PreviousSectorOffset);
    public uint FreeReferenceCount => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(GoldSectorInterestLayout.FreeReferenceCountOffset));
    public ReadOnlySpan<byte> Reserved => _payload.AsSpan(GoldSectorInterestLayout.ReservedOffset, GoldSectorInterestLayout.ReservedSize);
    public ushort GetEnteredSector(int index) => Slot(GoldSectorInterestLayout.EnteredSectorsOffset, index);
    public ushort GetLeftSector(int index) => Slot(GoldSectorInterestLayout.LeftSectorsOffset, index);
    public byte[] ToPayload() => _payload.ToArray();

    public GoldMessage CreateMessage(byte playerSlot)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        return new(GoldSectorInterestLayout.Opcode, playerSlot, playerSlot, 0, ToPayload());
    }

    public static GoldSectorInterestRequest FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldSectorInterestLayout.Size) throw new InvalidDataException("Gold sector interest requires exactly 62 bytes.");
        return new(payload.ToArray());
    }

    public static GoldSectorInterestRequest Create(ushort currentSector, ushort previousSector,
        ReadOnlySpan<ushort> entered, ReadOnlySpan<ushort> left, uint freeReferenceCount)
    {
        if (entered.Length != GoldSectorInterestLayout.SlotCount || left.Length != GoldSectorInterestLayout.SlotCount)
            throw new ArgumentException("Sector interest requires nine entered and nine left slots.");
        var bytes = new byte[GoldSectorInterestLayout.Size];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, currentSector);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), previousSector);
        for (var i = 0; i < GoldSectorInterestLayout.SlotCount; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldSectorInterestLayout.EnteredSectorsOffset + i * 2), entered[i]);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldSectorInterestLayout.LeftSectorsOffset + i * 2), left[i]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldSectorInterestLayout.FreeReferenceCountOffset), freeReferenceCount);
        return new(bytes);
    }

    private ushort Word(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(offset));
    private ushort Slot(int offset, int index) => (uint)index < GoldSectorInterestLayout.SlotCount
        ? Word(offset + index * 2) : throw new ArgumentOutOfRangeException(nameof(index));
}
