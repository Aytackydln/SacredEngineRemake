using System.Buffers.Binary;

namespace Sacred.GameServer.Wire;

/// <summary>TinCat frame constants and in-place header writing (matches Sacred.Network.TinCat).</summary>
public static class TinCatWire
{
    public const int HeaderBytes = 28;
    public const int MaximumPayloadBytes = 4 * 1024 * 1024;
    public const uint Magic = 0xDABAFBEF;
    public const uint UnassignedId = 0xEFFFFFEE;
    public const uint ServerId = 0xEFFFFFCC;
    public const ushort GameModule = 0x26B7;
    public const uint TimeAlignment = 1, CustomData = 2, LoginRequest = 3, LoginAccepted = 5, Logoff = 6, StayingAlive = 11;
    public const int LoginPayloadBytes = 52;

    /// <summary>Writes the 28-byte header for a payload already placed at <c>destination[HeaderBytes..]</c>.</summary>
    public static void SealFrame(Span<byte> destination, uint sender, uint recipient, uint type, int payloadLength)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], sender);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], recipient);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(destination[20..], payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], TinCatCrc.Compute(destination.Slice(HeaderBytes, payloadLength)));
    }
}
