using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Admission;

/// <summary>Gold game password: raw UTF-16LE code units without a terminator, opcode 0x12.</summary>
public static class GoldPasswordCodec
{
    // A remake ingress limit, not a recovered native textbox limit.
    public const int MaximumPasswordBytes = 1024;

    public static GoldMessage CreateMessage(string password, byte playerSlot, ushort sequence)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        if (password.Length > MaximumPasswordBytes / sizeof(ushort))
            throw new ArgumentOutOfRangeException(nameof(password));
        var payload = new byte[password.Length * sizeof(ushort)];
        for (var index = 0; index < password.Length; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(index * 2), password[index]);
        return new GoldMessage(0x12, playerSlot, playerSlot, sequence, payload);
    }
}
