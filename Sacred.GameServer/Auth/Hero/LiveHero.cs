namespace Sacred.GameServer.Auth.Hero;

/// <summary>Looks up the latest object body (from its 41-byte base block on) the server holds for a runtime reference.</summary>
public delegate bool ObjectBodyLookup(uint reference, out ReadOnlySpan<byte> body);

/// <summary>
/// What the server knows about one player's hero right now, captured from the player's own messages. Bodies start at the
/// object's 41-byte base block (the 8- or 24-byte message prefix is stripped).
/// </summary>
public sealed class LiveHero
{
    public byte Slot;
    /// <summary>Latest 0x169/0x16C/0x16E/0x16F state of the hero (compact creature values).</summary>
    public byte[]? Body; public int BodyLength;
    /// <summary>Latest 0x16B state of the hero (full creature values).</summary>
    public byte[]? FullBody; public int FullBodyLength;
    /// <summary>The full state arrived after the compact one.</summary>
    public bool FullIsNewer;
    /// <summary>Latest 0x122 inventory streams (without the leading inventory id) for the hero (id = slot) and the shared one (id 17).</summary>
    public byte[]? Inventory; public int InventoryLength;
    public byte[]? SharedInventory; public int SharedInventoryLength;
    /// <summary>The 100-byte statistics extension of the latest 0x14F (chunk CE).</summary>
    public byte[]? StatisticsExtension;
    public ObjectBodyLookup? Objects;
    public const int EquipmentSlots = 19;
    /// <summary>Equipment after 0x11E/0x11D changes; valid while <see cref="EquipmentKnown"/> (else the hero state's words count).</summary>
    public readonly uint[] Equipment = new uint[EquipmentSlots];
    public bool EquipmentKnown;

    /// <summary>Starts the equipment overlay from the newest hero state. False without a hero state.</summary>
    public bool LoadEquipment()
    {
        if (EquipmentKnown) return true;
        var body = FullIsNewer || Body is null ? HeroFullBody : HeroBody;
        const int offset = Sacred.Network.Gameplay.GoldCreatureStateLayout.EquipmentOffset;
        if (body.Length < offset + EquipmentSlots * 4) return false;
        for (var i = 0; i < EquipmentSlots; i++) Equipment[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(body[(offset + i * 4)..]);
        return EquipmentKnown = true;
    }

    public ReadOnlySpan<byte> HeroBody => Body is null ? default : Body.AsSpan(0, BodyLength);
    public ReadOnlySpan<byte> HeroFullBody => FullBody is null ? default : FullBody.AsSpan(0, FullBodyLength);
    public ReadOnlySpan<byte> InventoryStream => Inventory is null ? default : Inventory.AsSpan(0, InventoryLength);
    public ReadOnlySpan<byte> SharedInventoryStream => SharedInventory is null ? default : SharedInventory.AsSpan(0, SharedInventoryLength);

    public static void Store(ref byte[]? buffer, ref int length, ReadOnlySpan<byte> data)
    {
        if (buffer is null || buffer.Length < data.Length) buffer = new byte[Math.Max(data.Length, 4096)];
        data.CopyTo(buffer);
        length = data.Length;
    }

    public void Clear()
    {
        BodyLength = FullBodyLength = InventoryLength = SharedInventoryLength = 0;
        Body = FullBody = Inventory = SharedInventory = null;
        StatisticsExtension = null; FullIsNewer = false;
    }
}
