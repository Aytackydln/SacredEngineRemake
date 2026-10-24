namespace Sacred.Network.Gameplay;

public sealed class GoldIndexedRecordReplica
{
    public GoldIndexedScriptTable<GoldIndexedRecordState> Table { get; } = new();
    internal void Apply(GoldIndexedRecordEvent entry)
    {
        var decoded = GoldIndexedRecordCodec.Decode(entry.Message);
        Table.Set(decoded.Index, GoldIndexedRecordState.Patch(Table.Find(decoded.Index),
            decoded.Message.Payload.AsSpan(4, GoldIndexedRecordCodec.RecordSize)), 1);
    }
    internal void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode == GoldWorldBankLayout.Bank4Opcode && bank.Sections[6].Count != 0)
            Table.Replace(bank.Sections[6].Records.Select(record => GoldIndexedRecordState.FromBank(record.Data)));
    }
    internal void Clear() => Table.Clear();
}
