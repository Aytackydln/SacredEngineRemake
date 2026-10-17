using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Replace bank 2's movement-trigger section while retaining every other section/header/suffix byte.</summary>
public static class GoldMovementTriggerBankCodec
{
    public static GoldMessage Create(byte sender, byte origin, GoldWorldBankSnapshot template, IEnumerable<GoldPlannedMovementTrigger> plan)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(plan);
        if (template.Opcode != GoldMovementTriggerLayout.BankOpcode) throw new ArgumentException("Movement triggers require a bank 2 template.", nameof(template));
        var sectors = plan.GroupBy(piece => piece.Sector).OrderBy(group => group.Key).ToArray();
        var original = template.ToPayload();
        var prefixLength = GoldWorldBankLayout.HeaderSize + template.Sections[0].Records.Sum(record => record.Prefix.Length + sizeof(uint) + record.Data.Length)
            + template.Sections[1].Count * template.Sections[1].ElementSize;
        var previousLength = template.Sections[GoldMovementTriggerLayout.BankSection].Records.Sum(record => 2 * sizeof(uint) + record.Data.Length);
        using var output = new MemoryStream();
        output.Write(original.AsSpan(0, prefixLength));
        foreach (var sector in sectors)
        {
            var records = sector.ToArray();
            Write(sector.Key); Write(checked((uint)records.Length));
            foreach (var record in records) output.Write(record.Record.ToPayload());
        }
        output.Write(original.AsSpan(prefixLength + previousLength));
        if (output.Length > GoldMessageCodec.MaximumMessageBytes - GoldMessageCodec.HeaderBytes) throw new InvalidDataException("Movement-trigger bank exceeds the Gold message limit.");
        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldMovementTriggerLayout.BankSection * sizeof(uint)), checked((uint)sectors.Length));
        return GoldWorldBankCodec.Create(sender, origin, GoldWorldBankSnapshot.FromPayload(template.Opcode, bytes));
        void Write(uint value)
        {
            Span<byte> word = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(word, value); output.Write(word);
        }
    }
}
