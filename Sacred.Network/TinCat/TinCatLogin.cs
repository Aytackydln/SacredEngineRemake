using System.Buffers.Binary;
using System.Text;

namespace Sacred.Network.TinCat;

public static class TinCatLogin
{
    public const uint UnassignedId = 0xEFFFFFEE;
    public const uint ServerId = 0xEFFFFFCC;
    public const ushort GameModule = 0x26B7;
    public const uint LoginRequest = 3, LoginAccepted = 5, CustomData = 2, StayingAlive = 11;

    /// <summary>The 52-byte LOGMEON body at 0x10004380 / 0x10005E10.</summary>
    public static TinCatFrame CreateRequest(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 31 || name.Any(c => c is < ' ' or > '~'))
            throw new ArgumentException("TinCat names must contain 1..31 printable ASCII characters.", nameof(name));
        var payload = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, TinCatFrameCodec.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), UnassignedId);
        Encoding.ASCII.GetBytes(name, payload.AsSpan(8, 32));
        // The original transport's internal password is truncated to seven characters.
        "passwor"u8.CopyTo(payload.AsSpan(40));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(48), 1);
        return new TinCatFrame(UnassignedId, ServerId, LoginRequest, 0, payload);
    }

    public static uint ReadAccepted(TinCatFrame frame)
    {
        if (frame.Type != LoginAccepted || frame.Sender != ServerId || frame.Payload.Length != 52 ||
            BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload) != TinCatFrameCodec.Magic)
            throw new InvalidDataException("Invalid TinCat login acceptance.");
        var id = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4));
        if (id == 0 || id == UnassignedId || id == ServerId || frame.Recipient != id)
            throw new InvalidDataException("Invalid TinCat assigned identity.");
        return id;
    }
}
