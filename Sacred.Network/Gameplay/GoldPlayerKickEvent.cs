using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x157: client 0x7DCFAC disconnects only for sender zero.
/// Peer-originated notifications share the reason word but do not disconnect the receiver.</summary>
public sealed record GoldPlayerKickEvent(GoldMessage Message, uint Reason) : GoldGameEvent(Message)
{
    public const ushort Opcode = 0x157;
    public bool DisconnectsClient => Message.Sender == 0;

    /// <summary>Gold 0x7EB610's native error selections. Unmapped reasons remain numeric.</summary>
    public string Description => Reason switch
    {
        1 => "version mismatch",
        2 => "already online",
        3 => "invalid character level",
        4 => "invalid authentication",
        5 => "invalid message ID",
        6 => "incompatible game data",
        7 => "cheat protection",
        8 => "server kick",
        13 => "character is corrupted",
        14 => "invalid item",
        15 => "host kick",
        16 => "session is ending",
        17 => "idle timeout",
        _ => "unmapped native reason"
    };
}
