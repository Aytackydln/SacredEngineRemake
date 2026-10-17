using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldScriptVariableCodec
{
    public static GoldScriptVariableEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var indexed = message.Opcode switch
        {
            GoldScriptVariableLayout.AppendOpcode => false,
            GoldScriptVariableLayout.UpdateOpcode => true,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        if (message.Payload.Length != (indexed ? GoldScriptVariableLayout.UpdateSize : GoldScriptVariableLayout.RecordSize))
            throw new InvalidDataException("Invalid Gold script variable payload size.");
        uint? index = indexed ? BinaryPrimitives.ReadUInt32LittleEndian(message.Payload) : null;
        if (index > GoldScriptVariableLayout.MaximumUpdateIndex)
            throw new InvalidDataException("Script variable index overflows the native vector allocation.");
        return new(message, index,
            GoldScriptVariableRecord.FromPayload(message.Payload.AsSpan(indexed ? sizeof(uint) : 0)));
    }
    public static GoldMessage Create(byte sender, byte origin, GoldScriptVariableRecord variable, uint? index = null)
    {
        ArgumentNullException.ThrowIfNull(variable);
        var record = variable.ToPayload();
        if (index is not { } slot) return new(GoldScriptVariableLayout.AppendOpcode, sender, origin, 0, record);
        if (slot > GoldScriptVariableLayout.MaximumUpdateIndex) throw new ArgumentOutOfRangeException(nameof(index));
        var payload = new byte[GoldScriptVariableLayout.UpdateSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, slot);
        record.CopyTo(payload, sizeof(uint));
        return new(GoldScriptVariableLayout.UpdateOpcode, sender, origin, 0, payload);
    }
}
