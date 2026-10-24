using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldLobbyControlEvent(GoldMessage Message) : GoldLobbyEvent(Message);
public sealed record GoldServiceErrorEvent(GoldMessage Message, uint ErrorCode, uint Parameter) : GoldLobbyControlEvent(Message);
public sealed record GoldLobbyResultEvent(GoldMessage Message, uint Selector, uint ResultCode, string Text) : GoldLobbyControlEvent(Message);
public sealed record GoldPlayerSessionEvent(GoldMessage Message, uint? Reason) : GoldLobbyControlEvent(Message)
{
    public bool IsLogin => Message.Opcode == GoldLobbyControlCodec.LoginOpcode;
}
public sealed record GoldGameSettingsEvent(GoldMessage Message, ReadOnlyMemory<byte> Settings) : GoldLobbyControlEvent(Message);

public static class GoldLobbyControlCodec
{
    public const ushort ErrorOpcode = 0x009;
    public const ushort SettingsOpcode = 0x00C;
    public const ushort DepartureOpcode = 0x01C;
    public const ushort ResultOpcode = 0x024;
    public const ushort LoginResultOpcode = 0x026;
    public const ushort GameDepartureOpcode = 0x150;
    public const ushort LoginOpcode = 0x151;
    public static GoldLobbyControlEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            ErrorOpcode or DepartureOpcode or GameDepartureOpcode => 4,
            SettingsOpcode => 32, ResultOpcode or LoginResultOpcode => 10, LoginOpcode => 0,
            _ => throw new InvalidDataException("Not a Gold lobby control.")
        };
        if (message.Payload.Length < size || message.Opcode == ErrorOpcode && message.Payload.Length is > 4 and < 8)
            throw new InvalidDataException("Truncated Gold lobby control.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(offset));
        return message.Opcode switch
        {
            ErrorOpcode => new GoldServiceErrorEvent(owned, Word(0), owned.Payload.Length > 4 ? Word(4) : 0),
            SettingsOpcode => new GoldGameSettingsEvent(owned, owned.Payload.AsMemory(0, 32)),
            ResultOpcode or LoginResultOpcode => new GoldLobbyResultEvent(owned, Word(0), Word(4), GoldLobbyText.Read(owned.Payload.AsSpan(8))),
            _ => new GoldPlayerSessionEvent(owned, message.Opcode == LoginOpcode ? null : Word(0))
        };
    }
}
