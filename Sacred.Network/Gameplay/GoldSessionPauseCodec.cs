using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldSessionPauseEvent(GoldMessage Message) : GoldGameEvent(Message)
{
    public bool IsPaused => Message.Opcode == GoldSessionPauseCodec.PauseOpcode;
    public byte? PausedBy => IsPaused ? Message.Origin : null;
}

/// <summary>Gold pause/resume notifications consume no payload; Origin identifies the pausing player, or server zero.</summary>
public static class GoldSessionPauseCodec
{
    public const ushort ResumeOpcode = 0x134;
    public const ushort PauseOpcode = 0x135;
    public static GoldSessionPauseEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode is not (ResumeOpcode or PauseOpcode))
            throw new InvalidDataException("Not a Gold pause/resume notification.");
        return new(message with { Payload = message.Payload.ToArray() });
    }
}
