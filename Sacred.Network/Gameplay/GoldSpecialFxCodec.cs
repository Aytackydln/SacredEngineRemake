using System.Buffers.Binary;
using System.Numerics;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public readonly record struct GoldFxVectorBits(uint X, uint Y, uint Z)
{
    public Vector3 Value => new(BitConverter.UInt32BitsToSingle(X), BitConverter.UInt32BitsToSingle(Y), BitConverter.UInt32BitsToSingle(Z));
}
public abstract record GoldSpecialFxEvent(GoldMessage Message, uint ObjectReference) : GoldGameEvent(Message);
public sealed record GoldSpecialFxValueEvent(GoldMessage Message, uint ObjectReference, uint Value) : GoldSpecialFxEvent(Message, ObjectReference);
public sealed record GoldPingPongBallEvent(GoldMessage Message, uint ObjectReference, int X, int Y, byte Surface,
    GoldFxVectorBits Direction, GoldFxVectorBits ParticlePosition, uint ClockBits, bool SetsParticleFlag) : GoldSpecialFxEvent(Message, ObjectReference);
public sealed record GoldPacmanGhostEvent(GoldMessage Message, uint ObjectReference, int X, int Y, byte Surface,
    GoldFxVectorBits VectorData, uint FloatDataBits, uint IntegerData1, uint IntegerData2, uint IntegerData3) : GoldSpecialFxEvent(Message, ObjectReference);

/// <summary>Gold SPECIAL_PINGPONG_UPDATE_BATTER/BATTERAI/BALL and SPECIAL_PACMAN_UPDATE_GHOST.</summary>
public static class GoldSpecialFxCodec
{
    public const ushort BatterOpcode = 0x1A4;
    public const ushort BatterAiOpcode = 0x1A5;
    public const ushort BallOpcode = 0x1A6;
    public const ushort GhostOpcode = 0x1A9;
    public static GoldSpecialFxEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            BatterOpcode or BatterAiOpcode => 8, BallOpcode => 45, GhostOpcode => 44,
            _ => throw new InvalidDataException("Expected Gold special FX update.")
        };
        if (message.Payload.Length < size) throw new InvalidDataException("Truncated Gold special FX update.");
        var copy = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(copy.Payload.AsSpan(offset));
        GoldFxVectorBits Vector(int offset) => new(Word(offset), Word(offset + 4), Word(offset + 8));
        return message.Opcode switch
        {
            BatterOpcode or BatterAiOpcode => new GoldSpecialFxValueEvent(copy, Word(0), Word(4)),
            BallOpcode => new GoldPingPongBallEvent(copy, Word(0), unchecked((int)Word(4)), unchecked((int)Word(8)),
                unchecked((byte)Word(12)), Vector(16), Vector(28), Word(40), copy.Payload[44] != 0),
            _ => new GoldPacmanGhostEvent(copy, Word(0), unchecked((int)Word(4)), unchecked((int)Word(8)),
                unchecked((byte)Word(12)), Vector(16), Word(28), Word(32), Word(36), Word(40))
        };
    }
    /// <summary>Gold 0x84A1F4 truncates to Int64 and the caller retains its low UInt32.
    /// Invalid/overflow FISTP produces Int64.MinValue, whose low word is zero.</summary>
    public static uint ParticleClockWord(uint floatBits)
    {
        var value = (double)BitConverter.UInt32BitsToSingle(floatBits);
        return !double.IsFinite(value) || value >= 9223372036854775808d || value < -9223372036854775808d
            ? 0 : unchecked((uint)(long)Math.Truncate(value));
    }
}
