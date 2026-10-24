using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Tests.Infrastructure;

/// <summary>Independent wire fixtures: native cast bank and cObjectFX stream offsets, not production encoders.</summary>
internal static class SeraphimVisualPackets
{
    public static byte[] CastPayload(uint code, uint target)
    {
        var bytes = new byte[72];
        Word(bytes, 0, 500); Word(bytes, 4, 64); Word(bytes, 8, target);
        Word(bytes, 12, 2581); Word(bytes, 16, 180000); Word(bytes, 20, 135000);
        Word(bytes, 40, code);
        return bytes;
    }
    public static GoldMessage Cast(uint code, uint target) => new(0x183, 0, 1, 17, CastPayload(code, target));
    public static GoldMessage Creature(uint reference)
    {
        var bytes = new byte[445];
        Word(bytes, 0, 0xFEEDF00D); Word(bytes, 4, reference); Word(bytes, 8, 1);
        Word(bytes, 41, 0xF00DFACE); Word(bytes, 121, 0xC0DEF00D);
        return ObjectMessage(0x16E, bytes, 8);
    }
    public static byte[] FxPayload(uint resource, uint subtype, uint wearer = 501)
    {
        var bytes = new byte[227];
        Word(bytes, 0, 0xFEEDF00D); Word(bytes, 4, 600); Word(bytes, 8, resource);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 2581);
        Word(bytes, 18, 180000); Word(bytes, 22, 135000);
        Word(bytes, 41, 0xBAADC1FE); Word(bytes, 45, subtype);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(49), 1);
        Word(bytes, 53, 500); Word(bytes, 58, wearer); Word(bytes, 219, wearer);
        // Object duration is a separate field. The particle lifetime comes from CA rank/multiplier.
        Word(bytes, 83, 0x3F800000); // 1.0
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(151), 1);
        Word(bytes, 199, 0x3F800000); // sEventFXData +48: duration multiplier 1.
        return ObjectMessage(0x16F, bytes, 24).Payload;
    }
    public static GoldMessage Fx(uint resource, uint subtype, uint wearer = 501) => new(0x16F, 0, 1, 18, FxPayload(resource, subtype, wearer));
    public static GoldMessage Remove(uint reference)
    { var bytes = new byte[4]; Word(bytes, 0, reference); return new(0x166, 0, 1, 19, bytes); }
    public static void Word(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    private static GoldMessage ObjectMessage(ushort opcode, byte[] state, int prefix)
    {
        var bytes = new byte[prefix + state.Length];
        state.AsSpan(4, 8).CopyTo(bytes);
        if (prefix == 24)
        {
            state.AsSpan(18, 8).CopyTo(bytes.AsSpan(8));
            Word(bytes, 16, state[26]); Word(bytes, 20, BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(16)));
        }
        state.CopyTo(bytes, prefix);
        return new(opcode, 0, 1, 1, bytes);
    }
}
