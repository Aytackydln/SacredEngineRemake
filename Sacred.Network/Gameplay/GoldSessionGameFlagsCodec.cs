using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldSessionGameFlagsEvent(GoldMessage Message, uint Flags) : GoldGameEvent(Message);

/// <summary>Gold 0x01E replaces the connection game-flags word. It does not replace FIRSTCONTACT role flags.</summary>
public static class GoldSessionGameFlagsCodec
{
    public const ushort Opcode = 0x01E;
    public const uint LockMask = 4;
    /// <summary>Gold 7DB280, used by 42384A to enable multiplayer team hostility.</summary>
    public const uint PlayerVersusPlayerFlag = 0x40;
    public const uint BronzeDifficultyFlag = 0x100;
    public const uint SilverDifficultyFlag = 0x200;
    public const uint GoldDifficultyFlag = 0x400;
    public const uint PlatinumDifficultyFlag = 0x800;
    /// <summary>Gold 7D9AB2–7D9B07 resets to zero, then applies difficulty flags in ascending order.</summary>
    public static uint Difficulty(uint flags) => (flags & PlatinumDifficultyFlag) != 0 ? 4u :
        (flags & GoldDifficultyFlag) != 0 ? 3u : (flags & SilverDifficultyFlag) != 0 ? 2u :
        (flags & BronzeDifficultyFlag) != 0 ? 1u : 0u;
    public static GoldSessionGameFlagsEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Opcode != Opcode || message.Payload.Length < 4)
            throw new InvalidDataException("Gold game-flags notification requires a four-byte flags word.");
        return new(message with { Payload = message.Payload.ToArray() }, BinaryPrimitives.ReadUInt32LittleEndian(message.Payload));
    }
    /// <summary>Native global word 0x182EE48 after the active-client receive branch. Its wider gameplay meaning remains unmapped.</summary>
    public static ushort NativeControlWord(uint flags) => (ushort)((flags & 0x40010) == 0 || (flags & 0x40000) != 0 ? 1 : 0);
}
