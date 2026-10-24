using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

public sealed record GoldGameSettingsFields(uint Flags, uint Word20, uint Word178, uint Word9158, uint Word9DB8);
public sealed record GoldGameSettingsOperation(uint Id, int Value);
public sealed record GoldGameSettingsReceipt(GoldGameSettingsEvent Event, ushort ContextSector, bool? Direct,
    IReadOnlyList<GoldGameSettingsOperation> Operations);

/// <summary>00C resolves the local creature's native sector context before applying 41C0F0 settings.</summary>
public sealed class GoldGameSettingsReplica
{
    private GoldWorldReplica? _world;
    private Func<ushort, bool>? _isAvailable;
    private readonly List<GoldGameSettingsReceipt> _receipts = [];
    public GoldGameSettingsReplica() => Receipts = _receipts.AsReadOnly();
    public IReadOnlyList<GoldGameSettingsReceipt> Receipts { get; }
    public uint? NativeWord9E08 { get; private set; }
    public ushort? ContextSector { get; private set; }
    public GoldGameSettingsFields? Fields { get; private set; }
    public long IgnoredContextCount { get; private set; }
    internal void BindWorld(GoldWorldReplica world) => _world = world;
    /// <summary>Supply native sector availability (63D180), rather than assuming every announced sector is loaded.</summary>
    public void ConfigureSectorAvailability(Func<ushort, bool> predicate) => _isAvailable = predicate ?? throw new ArgumentNullException(nameof(predicate));
    private ushort FindSector()
    {
        if (_world is null) return 0;
        var local = _world.LocalPlayerSlot != 0 ? _world.LocalPlayerSlot : _world.Session.Teams.LocalPlayerSlot;
        if (local is < 1 or > 16 || !_world.Objects.TryGetValue(local, out var actor) || actor.Creature is null) return 0;
        if (actor.BaseState.Sector != 0) return actor.BaseState.Sector;
        var partner = actor.Creature.EquipmentReferences[SacredSavedCreatureLayout.SectorContextReferenceIndex];
        if ((actor.BaseState.Flags & 2) != 0 && partner != 0)
            return _world.Objects.TryGetValue(partner, out var mount) && mount.Creature is not null ? mount.BaseState.Sector : (ushort)0;
        return actor.BaseState.Owner != 0 && _world.Objects.TryGetValue(actor.BaseState.Owner, out var owner) ? owner.BaseState.Sector : (ushort)0;
    }
    internal void Apply(GoldGameSettingsEvent settings)
    {
        var sector = FindSector(); if (sector == 0) { IgnoredContextCount++; return; }
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(settings.Settings.Span[offset..]);
        NativeWord9E08 = Word(28); ContextSector = sector;
        bool? direct = _isAvailable?.Invoke(sector); GoldGameSettingsOperation[] operations = [];
        if (direct == true) Fields = new(Word(0) & ~0x100u, Word(4), Word(8), Word(12), Word(16));
        else if (direct == false)
            operations = [new(2, unchecked((int)Word(4)) / 30), new(3, Truncate(unchecked((int)Word(8)) * 0.5)),
                new(5, unchecked((int)Word(12)) / 30), new(7, Truncate(BitConverter.UInt32BitsToSingle(Word(16)) * 3.0))];
        _receipts.Add(new(settings, sector, direct, Array.AsReadOnly(operations)));
    }
    // The original helper stores an Int64 with x87 truncation, then uses EAX (its low word).
    // Invalid Int64 conversions produce 8000000000000000, whose low word is zero.
    private static int Truncate(double value) => double.IsNaN(value) || value >= 9223372036854775808d || value < -9223372036854775808d
        ? 0 : unchecked((int)(long)value);
    internal void Clear() { _receipts.Clear(); NativeWord9E08 = null; ContextSector = null; Fields = null; IgnoredContextCount = 0; }
}
