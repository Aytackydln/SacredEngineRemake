namespace Sacred.Network.Gameplay;

/// <summary>The native singleton is created lazily, including when an incremental update precedes a bank.
/// Import replaces its records; retained bank snapshots remain immutable.</summary>
public sealed class GoldWorldRegionReplica
{
    public GoldWorldRegionTable? Table { get; private set; }
    public void Apply(GoldWorldBankSnapshot bank)
    {
        ArgumentNullException.ThrowIfNull(bank);
        if (bank.Opcode == GoldWorldBankLayout.Bank1Opcode)
            Table = GoldWorldRegionTable.FromPayload(bank.Globals!.Table);
    }
    public void Apply(GoldWorldRegionFlagsEvent update)
    {
        ArgumentNullException.ThrowIfNull(update);
        Table = (Table ?? GoldWorldRegionTable.CreateInitial()).WithFlags(update.Region, update.Mask, update.Set);
    }
    public void Apply(GoldGameEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry is GoldWorldRegionFlagsEvent flags) Apply(flags);
        else if (entry is GoldWorldRegionValueEvent)
        {
            var update = GoldWorldRegionValueCodec.Decode(entry.Message);
            Table = GoldWorldRegionValueReducer.Apply(Table ?? GoldWorldRegionTable.CreateInitial(), update);
        }
        else throw new ArgumentException("Expected a Gold region update.", nameof(entry));
    }
    public void Clear() => Table = null;
}
