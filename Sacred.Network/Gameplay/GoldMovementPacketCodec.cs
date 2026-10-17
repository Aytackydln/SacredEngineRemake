using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Gold movement wire construction. Navigation, ownership and native action/timing selection belong to the caller.</summary>
public static class GoldMovementPacketCodec
{
    public static GoldMovementBody CreatePath(uint word1F8, uint flags200, uint pathIndex,
        ReadOnlySpan<GoldMovementPoint> path, byte mode)
    {
        var bytes = new byte[checked(GoldMovementLayout.PathOffset + path.Length * GoldMovementLayout.PathPointSize + 1)];
        Header(bytes, word1F8, flags200);
        Word(bytes, GoldMovementLayout.PathIndexOffset, pathIndex);
        Word(bytes, GoldMovementLayout.PathCountOffset, (uint)path.Length);
        for (var i = 0; i < path.Length; i++)
        {
            var offset = GoldMovementLayout.PathOffset + i * GoldMovementLayout.PathPointSize;
            Word(bytes, offset, unchecked((uint)path[i].X));
            Word(bytes, offset + 4, unchecked((uint)path[i].Y));
        }
        bytes[^1] = mode;
        return GoldMovementBody.FromPayload(GoldMovementLayout.PathOpcode, bytes);
    }

    public static GoldMovementBody CreateAction(uint word1F8, uint flags200, ReadOnlySpan<byte> action, byte mode)
    {
        if (action.Length != GoldMovementLayout.ActionSize) throw new ArgumentException("Gold action block requires exactly 173 bytes.", nameof(action));
        var bytes = new byte[GoldMovementLayout.ActionBodySize];
        Header(bytes, word1F8, flags200);
        action.CopyTo(bytes.AsSpan(GoldMovementLayout.ActionOffset));
        bytes[^1] = mode;
        return GoldMovementBody.FromPayload(GoldMovementLayout.ActionOpcode, bytes);
    }

    public static GoldMessage CreateMovement(byte playerSlot, uint reference, uint control174,
        GoldObjectPosition position, GoldMovementBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var encodedBody = body.ToPayload();
        var payload = new byte[GoldMovementLayout.PrefixSize + encodedBody.Length];
        Word(payload, 0, reference);
        Word(payload, GoldMovementLayout.Control174Offset, control174);
        Position(payload, GoldMovementLayout.XOffset, position);
        encodedBody.CopyTo(payload, GoldMovementLayout.PrefixSize);
        return Message(playerSlot, reference, body.Opcode, payload);
    }

    public static GoldMessage CreateEnd(byte playerSlot, uint reference, GoldObjectPosition position, uint facingBits, uint retainedWord)
    {
        var payload = new byte[GoldMovementLayout.EndRetainedOffset + 4];
        Word(payload, 0, reference);
        Position(payload, GoldMovementLayout.EndXOffset, position);
        Word(payload, GoldMovementLayout.EndFacingOffset, facingBits);
        Word(payload, GoldMovementLayout.EndRetainedOffset, retainedWord);
        return Message(playerSlot, reference, GoldMovementLayout.EndOpcode, payload);
    }

    private static GoldMessage Message(byte slot, uint reference, ushort opcode, byte[] payload)
    {
        if (slot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(slot));
        if (reference == 0) throw new ArgumentOutOfRangeException(nameof(reference));
        return new(opcode, slot, slot, 0, payload);
    }

    private static void Header(byte[] bytes, uint mode, uint flags)
    {
        Word(bytes, 0, GoldMovementLayout.Marker);
        Word(bytes, GoldMovementLayout.Word1F8Offset, mode);
        Word(bytes, GoldMovementLayout.Flags200Offset, flags);
    }
    private static void Position(byte[] bytes, int offset, GoldObjectPosition position)
    {
        Word(bytes, offset, unchecked((uint)position.X));
        Word(bytes, offset + 4, unchecked((uint)position.Y));
        Word(bytes, offset + 8, position.Surface);
        Word(bytes, offset + 12, position.Sector);
    }
    private static void Word(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
