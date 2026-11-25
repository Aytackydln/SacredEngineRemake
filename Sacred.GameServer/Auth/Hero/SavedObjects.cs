using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Network.Gameplay;

namespace Sacred.GameServer.Auth.Hero;

/// <summary>
/// The saved-object list (chunk C8: u32 count, then per record u32 resource (0 = empty) + object stream) and the conversion of
/// a live item state into its saved record. Records are told apart by their marker at +247 (items 0xBAADC0DE, creatures
/// 0xC0DEF00D), so no Items.pak category lookup is needed.
/// <para>Item streams (Sacred.Core <c>SacredSavedItemLayout</c> / Sacred.Network <c>GoldItemStateLayout</c>):</para>
/// <list type="table">
/// <item><term>file</term><description>247-byte saved physical | 0xBAADC0DE | <b>u8</b> record count | 130-byte records | 130-byte item data |
/// trailer 0, 0, 0xBAADC0DE | [0xBAADF00D + 4 words for weapons]</description></item>
/// <item><term>wire</term><description>121-byte physical | 0xBAADC0DE | <b>u16</b> record count | records | item data | trailer [+0x1E0 word]
/// 0xBAADC0DE | [weapon extension]</description></item>
/// </list>
/// The records are the item's merged value records: the item itself first, then one per socketed rune or gem, in socket order.
/// They are copied verbatim in their original order; the file can only hold 255 of them (an item with more is refused rather
/// than truncated, which would silently drop sockets).
/// </summary>
public static class SavedObjects
{
    public const int PhysicalBytes = SacredSavedPhysicalLayout.Size, MarkerOffset = 247, ItemCountOffset = 251, ItemRecordsOffset = 252;
    public const int RecordBytes = 130, ItemMinimumBytes = SacredSavedItemLayout.MinimumSize, WeaponBytes = 20, TrailerBytes = 12;
    public const uint ItemMarker = 0xBAADC0DE, WeaponMarker = 0xBAADF00D, CreatureMarker = 0xC0DEF00D;
    private const int BaseReference = SacredSavedObjectBaseLayout.ObjectReferenceOffset;
    /// <summary>Saved physical bytes 18..32: the base flags, the sector, the position and the surface (see <see cref="WriteItem"/>).</summary>
    private const int ContainedStateOffset = 18, ContainedStateLength = 15;

    /// <summary>Size of the object stream starting at <paramref name="data"/> (after the resource word), or -1.</summary>
    public static int RecordSize(ReadOnlySpan<byte> data, byte version)
    {
        if (data.Length < MarkerOffset + 4) return -1;
        var marker = BinaryPrimitives.ReadUInt32LittleEndian(data[MarkerOffset..]);
        if (marker == ItemMarker)
        {
            if (data.Length < ItemMinimumBytes) return -1;
            var size = ItemMinimumBytes + data[ItemCountOffset] * RecordBytes;
            if (size > data.Length) return -1;
            if (data.Length >= size + 4 && BinaryPrimitives.ReadUInt32LittleEndian(data[size..]) == WeaponMarker) size += WeaponBytes;
            return size <= data.Length ? size : -1;
        }
        if (marker != CreatureMarker) return -1;
        try { return SacredSavedCreature.FromPayload(data, version).SerializedSize; }
        catch (InvalidDataException) { return -1; }
    }

    public static bool IsItem(ReadOnlySpan<byte> data) =>
        data.Length >= MarkerOffset + 4 && BinaryPrimitives.ReadUInt32LittleEndian(data[MarkerOffset..]) == ItemMarker;

    public static uint Reference(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32LittleEndian(data[BaseReference..]);

    /// <summary>The 130-byte item data plus records of a saved item (what identifies its content).</summary>
    public static ReadOnlySpan<byte> SavedContent(ReadOnlySpan<byte> saved) =>
        saved.Slice(ItemCountOffset + 1, saved[ItemCountOffset] * RecordBytes + RecordBytes);

    // ---- live (wire) item states -------------------------------------------------------------------------------------

    /// <summary>Checks a live item body (from its 41-byte base block on) and returns its record count and whether it has the
    /// weapon extension; false when the body is not a complete item state.</summary>
    public static bool ReadLiveItem(ReadOnlySpan<byte> body, out int records, out bool weapon)
    {
        records = 0; weapon = false;
        if (body.Length < GoldItemStateLayout.MinimumSize) return false;
        if (BinaryPrimitives.ReadUInt32LittleEndian(body) != GoldObjectStateLayout.Marker ||
            BinaryPrimitives.ReadUInt32LittleEndian(body[GoldItemStateLayout.MarkerOffset..]) != ItemMarker) return false;
        records = BinaryPrimitives.ReadUInt16LittleEndian(body[GoldItemStateLayout.CountOffset..]);
        var end = GoldItemStateLayout.RecordsOffset + (records + 1) * RecordBytes + GoldItemStateLayout.TrailerSize;
        if (body.Length < end) return false;
        weapon = body.Length >= end + WeaponBytes && BinaryPrimitives.ReadUInt32LittleEndian(body[end..]) == WeaponMarker;
        return true;
    }

    public static ReadOnlySpan<byte> LiveContent(ReadOnlySpan<byte> body, int records) =>
        body.Slice(GoldItemStateLayout.RecordsOffset, (records + 1) * RecordBytes);

    /// <summary>
    /// Writes the saved record (object stream, without the resource word) of a live item. <paramref name="template"/> is a saved
    /// item to take the file-only fields from (node header, world links, model/animation words, flags the loader clears): the
    /// baseline record of the same item when there is one, otherwise any saved item. When the live physical state is what the
    /// loader would have made of the template, the template's 247 bytes are kept unchanged. Returns the size, or -1.
    /// </summary>
    public static int WriteItem(ReadOnlySpan<byte> body, uint fileReference, ReadOnlySpan<byte> template, Span<byte> destination)
    {
        if (!ReadLiveItem(body, out var records, out var weapon) || records > byte.MaxValue || template.Length < PhysicalBytes) return -1;
        var size = ItemMinimumBytes + records * RecordBytes + (weapon ? WeaponBytes : 0);
        if (destination.Length < size) return -1;
        var physical = destination[..PhysicalBytes];
        template[..PhysicalBytes].CopyTo(physical);
        if (!SamePhysical(template[..PhysicalBytes], body[..GoldPhysicalStateLayout.Size])) ApplyPhysical(body, physical);
        // Everything written here is held by the hero (inventory, shared inventory, equipment): where the object was last seen in the world
        // and the flags of a world object mean nothing for it, and the loader treats an item that carries them as lying there instead of
        // in the backpack. An item made on the client (Gear's import: created at the hero's feet, announced, then put into a container)
        // still has its creation state on the wire, so its saved sector, position, surface and flags are the template's, which is an item
        // of the same kind that was in a container when saved.
        template.Slice(ContainedStateOffset, ContainedStateLength).CopyTo(physical[ContainedStateOffset..]);
        BinaryPrimitives.WriteUInt32LittleEndian(physical[BaseReference..], fileReference);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[MarkerOffset..], ItemMarker);
        destination[ItemCountOffset] = (byte)records;
        LiveContent(body, records).CopyTo(destination[ItemRecordsOffset..]);
        var trailer = destination.Slice(ItemRecordsOffset + (records + 1) * RecordBytes, TrailerBytes);
        trailer.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(trailer[8..], ItemMarker);
        if (weapon)
            body.Slice(GoldItemStateLayout.RecordsOffset + (records + 1) * RecordBytes + GoldItemStateLayout.TrailerSize, WeaponBytes)
                .CopyTo(destination[(size - WeaponBytes)..]);
        return size;
    }

    /// <summary>True when <paramref name="live"/> (121-byte physical) is what the participant loader makes of the saved physical
    /// (Sacred.Network <c>GoldSavedPhysicalStateImporter.ImportParticipantObject</c>), ignoring reference, owner and the flag
    /// bits it changes.</summary>
    private static bool SamePhysical(ReadOnlySpan<byte> saved, ReadOnlySpan<byte> live)
    {
        GoldPhysicalState imported;
        try { imported = GoldSavedPhysicalStateImporter.ImportRetainedState(saved); }
        catch (InvalidDataException) { return false; }
        Span<byte> expected = stackalloc byte[GoldPhysicalStateLayout.Size];
        imported.ToPayload().CopyTo(expected);
        Span<byte> actual = stackalloc byte[GoldPhysicalStateLayout.Size];
        live.CopyTo(actual);
        Neutralize(expected); Neutralize(actual);
        return expected.SequenceEqual(actual);
    }

    private static void Neutralize(Span<byte> span)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(span[GoldObjectStateLayout.ReferenceOffset..], 0);
        span[GoldObjectStateLayout.OwnerOffset] = 0;
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(span[GoldObjectStateLayout.FlagsOffset..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[GoldObjectStateLayout.FlagsOffset..], flags & ~LoaderFlagBits);
    }

    /// <summary>Base flag bits the file/participant loaders change; on save they are taken from the template.</summary>
    private const uint LoaderFlagBits = 0x04001000u | 0x400000u | 0x200u | 0x8000u | 0x200010u | 0x30u;

    /// <summary>Overlays the fields the wire carries onto a saved physical (the inverse of the projection in
    /// <c>GoldSavedPhysicalStateImporter.ImportRetainedState</c>).</summary>
    private static void ApplyPhysical(ReadOnlySpan<byte> body, Span<byte> saved)
    {
        var baseFlags = BinaryPrimitives.ReadUInt32LittleEndian(saved[18..]);
        var liveFlags = BinaryPrimitives.ReadUInt32LittleEndian(body[GoldObjectStateLayout.FlagsOffset..]);
        BinaryPrimitives.WriteUInt32LittleEndian(saved[18..], (liveFlags & ~LoaderFlagBits) | (baseFlags & LoaderFlagBits));
        BinaryPrimitives.WriteUInt32LittleEndian(saved[14..], BinaryPrimitives.ReadUInt32LittleEndian(body[GoldObjectStateLayout.TypeOffset..]));
        body.Slice(GoldObjectStateLayout.SectorOffset, 2).CopyTo(saved[22..]);
        body.Slice(GoldObjectStateLayout.XOffset, 8).CopyTo(saved[24..]);
        saved[32] = body[GoldObjectStateLayout.SurfaceOffset];
        body.Slice(GoldObjectStateLayout.Word28Offset, 4).CopyTo(saved[33..]);
        body.Slice(GoldObjectStateLayout.Word34Offset, 4).CopyTo(saved[45..]);
        body.Slice(GoldObjectStateLayout.Word3COffset, 4).CopyTo(saved[51..]);
        body.Slice(GoldPhysicalStateLayout.Word64Offset, 20).CopyTo(saved[SacredSavedPhysicalLayout.Word64Offset..]);
        body.Slice(GoldPhysicalStateLayout.Vector70Offset, 12).CopyTo(saved[SacredSavedPhysicalLayout.Vector70Offset..]);
        body.Slice(GoldPhysicalStateLayout.VectorD8Offset, 24).CopyTo(saved[SacredSavedPhysicalLayout.VectorD8Offset..]);
        body.Slice(GoldPhysicalStateLayout.Word94Offset, 4).CopyTo(saved[SacredSavedPhysicalLayout.Word94Offset..]);
        var savedB0 = BinaryPrimitives.ReadUInt32LittleEndian(saved[SacredSavedPhysicalLayout.WordB0Offset..]);
        var liveB0 = BinaryPrimitives.ReadUInt32LittleEndian(body[GoldPhysicalStateLayout.WordB0Offset..]);
        BinaryPrimitives.WriteUInt32LittleEndian(saved[SacredSavedPhysicalLayout.WordB0Offset..], (liveB0 & ~4u) | (savedB0 & 4u));
    }
}
