using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldScriptResourceEvent(GoldMessage Message, string Name, uint? ResourceId) : GoldGameEvent(Message);

public static class GoldScriptResourceCodec
{
    public static GoldScriptResourceEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var offset = message.Opcode switch
        {
            GoldScriptResourceLayout.RegisterNameOpcode => 4,
            GoldScriptResourceLayout.RemoveNameOpcode => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        var bytes = message.Payload.AsSpan();
        if (bytes.Length <= offset) throw new InvalidDataException("Missing script-name payload.");
        var end = bytes[offset..].IndexOf((byte)0);
        if (end < 0 || end >= GoldScriptResourceLayout.NameSize) throw new InvalidDataException("Script name has no bounded terminator.");
        return new(message with { Payload = bytes.ToArray() }, GoldScriptNames.Read(bytes.Slice(offset, end)),
            offset == 0 ? null : BinaryPrimitives.ReadUInt32LittleEndian(bytes));
    }

    public static GoldMessage Create(byte sender, byte origin, string name, uint? resourceId)
    {
        var encoded = GoldScriptNames.Encode(name);
        var bytes = new byte[encoded.Length + (resourceId is null ? 0 : 4)];
        if (resourceId is { } resource) BinaryPrimitives.WriteUInt32LittleEndian(bytes, resource);
        encoded.CopyTo(bytes, bytes.Length - encoded.Length);
        return new(resourceId is null ? GoldScriptResourceLayout.RemoveNameOpcode : GoldScriptResourceLayout.RegisterNameOpcode,
            sender, origin, 0, bytes);
    }
}
