using System.Buffers.Binary;
using System.Collections.ObjectModel;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldScriptDialogEvent(GoldMessage Message, uint Index, SacredScriptDialog Record) : GoldGameEvent(Message);

/// <summary>Received dialog definitions. No quest code is executed to duplicate the host's state.</summary>
public sealed class GoldScriptDialogReplica
{
    public const ushort UpdateOpcode = 0x1C8;
    private readonly Dictionary<uint, SacredScriptDialog> _records = [];
    public GoldScriptDialogReplica() => Records = new ReadOnlyDictionary<uint, SacredScriptDialog>(_records);
    public IReadOnlyDictionary<uint, SacredScriptDialog> Records { get; }
    public uint? GetIcon(uint index) => _records.TryGetValue(index, out var record) ? record.Icon : null;
    public void Apply(GoldWorldBankSnapshot bank)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank4Opcode || bank.Sections[0].Records.Count == 0) return;
        var records = bank.Sections[0].Records.Select(row => SacredScriptDialog.FromPayload(row.Data)).ToArray();
        _records.Clear();
        for (uint index = 0; index < records.Length; index++) _records.Add(index, records[index]);
    }
    public void Apply(GoldScriptDialogEvent update) => _records[update.Index] = update.Record;
    public static GoldScriptDialogEvent Decode(GoldMessage message)
    {
        if (message.Opcode != UpdateOpcode || message.Payload.Length < 4 + SacredScriptDialogLayout.SerializedSize)
            throw new InvalidDataException("Gold dialog update requires a slot word and an 80-byte record.");
        var record = SacredScriptDialog.FromPayload(message.Payload.AsSpan(4, SacredScriptDialogLayout.SerializedSize));
        return new(message with { Payload = message.Payload.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload), record);
    }
    public void Clear() => _records.Clear();
}
