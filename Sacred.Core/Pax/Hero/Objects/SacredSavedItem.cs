using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Core.Pax.Hero.Objects;

public sealed class SacredSavedItem
{
    private readonly byte[] _payload;
    private readonly int _itemDataOffset;
    private SacredSavedItem(byte[] payload, int count, bool weapon) => (_payload, Count, HasWeaponExtension, _itemDataOffset) =
        (payload, count, weapon, SacredSavedItemLayout.RecordsOffset + count * SacredSavedItemLayout.ItemDataSize);
    public SacredSavedObjectBaseLayout BaseState => MemoryMarshal.Read<SacredSavedObjectBaseLayout>(_payload);
    public ReadOnlySpan<byte> PhysicalData => _payload.AsSpan(0, SacredSavedPhysicalLayout.Size);
    public int Count { get; }
    public int SerializedSize => _payload.Length;
    public bool HasWeaponExtension { get; }
    public ReadOnlySpan<byte> ItemData => _payload.AsSpan(_itemDataOffset, SacredSavedItemLayout.ItemDataSize);
    public uint ItemResource => BinaryPrimitives.ReadUInt32LittleEndian(ItemData);
    public uint ItemFlags => BinaryPrimitives.ReadUInt32LittleEndian(ItemData[4..]);
    public SacredEquipmentInstanceLayout Instance => MemoryMarshal.Read<SacredEquipmentInstanceLayout>(ItemData);
    public ReadOnlySpan<byte> Trailer => _payload.AsSpan(_itemDataOffset + SacredSavedItemLayout.ItemDataSize, SacredSavedItemLayout.TrailerSize);
    public ReadOnlySpan<byte> WeaponExtension => HasWeaponExtension ? _payload.AsSpan(_payload.Length - SacredSavedItemLayout.WeaponExtensionSize) : ReadOnlySpan<byte>.Empty;
    public ReadOnlySpan<byte> GetRecord(int index) => SacredSavedStreamBounds.Record(_payload, index, Count, SacredSavedItemLayout.RecordsOffset, SacredSavedItemLayout.ItemDataSize);
    public SacredEquipmentInstanceLayout GetInstanceRecord(int index) => MemoryMarshal.Read<SacredEquipmentInstanceLayout>(GetRecord(index));
    public byte[] ToPayload() => _payload.ToArray();
    public static SacredSavedItem FromPayload(ReadOnlySpan<byte> payload, byte version, bool weaponExtension)
    {
        SacredSavedStreamBounds.RequireVersion(version);
        SacredSavedStreamBounds.Require(payload, SacredSavedItemLayout.MinimumSize);
        var count = payload[SacredSavedItemLayout.CountOffset];
        var size = SacredSavedItemLayout.MinimumSize + count * SacredSavedItemLayout.ItemDataSize + (weaponExtension ? SacredSavedItemLayout.WeaponExtensionSize : 0);
        SacredSavedStreamBounds.Require(payload, size);
        return new(payload[..size].ToArray(), count, weaponExtension);
    }
}
