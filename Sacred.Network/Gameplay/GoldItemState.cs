using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.Pak.Weapon;

namespace Sacred.Network.Gameplay;

/// <summary>Packet item values and optional weapon extension. This does not import a saved item.</summary>
public sealed class GoldItemState
{
    private readonly byte[] _payload;
    private readonly int _itemDataOffset;
    private readonly int _trailerOffset;

    private GoldItemState(byte[] payload, (ushort Count, int Trailer, bool Weapon) bounds)
    {
        _payload = payload;
        Count = bounds.Count;
        _itemDataOffset = GoldItemStateLayout.RecordsOffset + Count * GoldItemStateLayout.ItemDataSize;
        _trailerOffset = bounds.Trailer;
        HasWeaponExtension = bounds.Weapon;
    }

    public GoldPhysicalState PhysicalState => GoldPhysicalState.FromPayload(_payload);
    public ushort Count { get; }
    public ReadOnlySpan<byte> Records => _payload.AsSpan(GoldItemStateLayout.RecordsOffset, Count * GoldItemStateLayout.ItemDataSize);
    public ReadOnlySpan<byte> ItemData => _payload.AsSpan(_itemDataOffset, GoldItemStateLayout.ItemDataSize);
    public SacredEquipmentInstanceLayout Instance => MemoryMarshal.Read<SacredEquipmentInstanceLayout>(ItemData);
    /// <summary>Native importer clamps this runtime word to zero if greater than 17; retain the wire value.</summary>
    public uint Word1E0 => Word(_trailerOffset + GoldItemStateLayout.Word1E0Offset);
    /// <summary>Runtime +0x1E0: inventory membership, set/read by native property commands 0x0B/0x0C; zero is unbound.</summary>
    public uint InventoryId => Word1E0;
    public uint RepeatedMarker => Word(_trailerOffset + GoldItemStateLayout.RepeatedMarkerOffset);
    public bool HasWeaponExtension { get; }
    public ReadOnlySpan<byte> WeaponExtension => HasWeaponExtension
        ? _payload.AsSpan(_trailerOffset + GoldItemStateLayout.TrailerSize, GoldItemStateLayout.WeaponExtensionSize)
        : ReadOnlySpan<byte>.Empty;
    public int SerializedSize => _trailerOffset + GoldItemStateLayout.TrailerSize + (HasWeaponExtension ? GoldItemStateLayout.WeaponExtensionSize : 0);
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(SerializedSize);

    public ReadOnlySpan<byte> GetRecord(int index)
    {
        if ((uint)index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return Records.Slice(index * GoldItemStateLayout.ItemDataSize, GoldItemStateLayout.ItemDataSize);
    }

    public SacredEquipmentInstanceLayout GetInstanceRecord(int index) => MemoryMarshal.Read<SacredEquipmentInstanceLayout>(GetRecord(index));

    public static GoldItemState FromPayload(ReadOnlySpan<byte> payload)
    {
        var bounds = Validate(payload);
        return new(payload.ToArray(), bounds);
    }
    public byte[] ToPayload() => _payload.ToArray();

    /// <summary>Serialize supplied runtime values. Caller owns import normalization and reference/owner policy.</summary>
    public static GoldItemState Create(GoldPhysicalState physical, ReadOnlySpan<byte> records,
        ReadOnlySpan<byte> itemData, uint word1E0, ReadOnlySpan<byte> weaponExtension = default)
    {
        ArgumentNullException.ThrowIfNull(physical);
        if (!physical.RemainingData.IsEmpty) throw new ArgumentException("Physical prefix must contain exactly 121 bytes.", nameof(physical));
        if (records.Length % GoldItemStateLayout.ItemDataSize != 0 || records.Length / GoldItemStateLayout.ItemDataSize > ushort.MaxValue)
            throw new ArgumentException("Item records must be complete 130-byte records with a 16-bit count.", nameof(records));
        if (itemData.Length != GoldItemStateLayout.ItemDataSize) throw new ArgumentException("Item data requires 130 bytes.", nameof(itemData));
        if (!weaponExtension.IsEmpty && (weaponExtension.Length != GoldItemStateLayout.WeaponExtensionSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(weaponExtension) != GoldItemStateLayout.WeaponMarker))
            throw new ArgumentException("Weapon extension requires its marker and four words.", nameof(weaponExtension));
        var payload = new byte[GoldItemStateLayout.MinimumSize + records.Length + weaponExtension.Length];
        physical.ToPayload().CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(GoldItemStateLayout.MarkerOffset), GoldItemStateLayout.Marker);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(GoldItemStateLayout.CountOffset), (ushort)(records.Length / GoldItemStateLayout.ItemDataSize));
        records.CopyTo(payload.AsSpan(GoldItemStateLayout.RecordsOffset));
        var itemOffset = GoldItemStateLayout.RecordsOffset + records.Length;
        itemData.CopyTo(payload.AsSpan(itemOffset));
        var trailer = itemOffset + GoldItemStateLayout.ItemDataSize;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(trailer), word1E0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(trailer + 4), GoldItemStateLayout.Marker);
        weaponExtension.CopyTo(payload.AsSpan(trailer + GoldItemStateLayout.TrailerSize));
        return FromPayload(payload);
    }

    internal static (ushort Count, int Trailer, bool Weapon) Validate(ReadOnlySpan<byte> payload)
    {
        GoldPhysicalState.Validate(payload);
        if (!GoldObjectStateReader.HasMarker(payload, GoldItemStateLayout.MarkerOffset, GoldItemStateLayout.Marker))
            throw new InvalidDataException("Unsupported Gold item state marker.");
        if (payload.Length < GoldItemStateLayout.MinimumSize) throw new InvalidDataException("Truncated Gold item state.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(payload[GoldItemStateLayout.CountOffset..]);
        var trailer = GoldItemStateLayout.RecordsOffset + count * GoldItemStateLayout.ItemDataSize + GoldItemStateLayout.ItemDataSize;
        var end = trailer + GoldItemStateLayout.TrailerSize;
        if (payload.Length < end) throw new InvalidDataException("Truncated Gold item records or trailer.");
        var weapon = GoldObjectStateReader.HasMarker(payload, end, GoldItemStateLayout.WeaponMarker);
        if (weapon && payload.Length < end + GoldItemStateLayout.WeaponExtensionSize)
            throw new InvalidDataException("Truncated Gold weapon extension.");
        return (count, trailer, weapon);
    }

    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));
}
