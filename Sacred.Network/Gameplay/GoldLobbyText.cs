using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

internal static class GoldLobbyText
{
    public static string Read(ReadOnlySpan<byte> bytes)
    {
        for (var offset = 0; offset + 1 < bytes.Length; offset += 2)
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]) != 0) continue;
            var chars = new char[offset / 2];
            for (var i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes[(i * 2)..]);
            return new string(chars);
        }
        throw new InvalidDataException("Unterminated Gold lobby UTF-16 text.");
    }
}
