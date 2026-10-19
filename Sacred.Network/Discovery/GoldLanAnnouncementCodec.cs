using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;

namespace Sacred.Network.Discovery;

/// <summary>Gold's four-byte discovery prefix, separate from the ten-byte gameplay envelope.</summary>
public static class GoldLanAnnouncementCodec
{
    public static GoldLanAnnouncement Decode(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < GoldLanAnnouncementLayout.HeaderBytes)
            throw new InvalidDataException("Truncated LAN announcement header.");
        var header = BinaryPrimitives.ReadUInt32LittleEndian(datagram);
        if ((header & ~GoldLanAnnouncementLayout.RawFlag) != GoldLanAnnouncementLayout.PayloadBytes)
            throw new InvalidDataException("Invalid LAN announcement size.");
        var body = datagram[GoldLanAnnouncementLayout.HeaderBytes..];
        var payload = new byte[GoldLanAnnouncementLayout.PayloadBytes];
        if ((header & GoldLanAnnouncementLayout.RawFlag) != 0)
        {
            if (body.Length != payload.Length) throw new InvalidDataException("Truncated raw LAN announcement.");
            body.CopyTo(payload);
            var previous = (uint)payload.Length;
            for (var offset = 0; offset + 4 <= payload.Length; offset += 4)
            {
                var cipher = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(offset));
                BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset), unchecked(previous + 0xB5D6C7A3) ^ cipher);
                previous = cipher;
            }
            // The remaining two bytes are plain, as in native UncompressMemory (0x491B00).
        }
        else
        {
            using var input = new MemoryStream(body.ToArray(), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            try { zlib.ReadExactly(payload); }
            catch (EndOfStreamException error) { throw new InvalidDataException("Truncated compressed LAN announcement.", error); }
            if (zlib.ReadByte() != -1) throw new InvalidDataException("LAN announcement exceeds its declared size.");
        }
        var port = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(GoldLanAnnouncementLayout.TcpPortOffset));
        if (port == 0) throw new InvalidDataException("LAN announcement has no TCP port.");
        var address = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(GoldLanAnnouncementLayout.AddressOffset));
        var ip = new IPAddress(new byte[] { (byte)(address >> 24), (byte)(address >> 16), (byte)(address >> 8), (byte)address });
        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast) || (address >> 28) == 0xE)
            throw new InvalidDataException("LAN announcement has an invalid server address.");
        var name = Encoding.Unicode.GetString(payload, GoldLanAnnouncementLayout.NameOffset,
            GoldLanAnnouncementLayout.NameCharacters * 2);
        var terminator = name.IndexOf('\0');
        if (terminator >= 0) name = name[..terminator];
        return new(BinaryPrimitives.ReadUInt16LittleEndian(payload), new(ip, port),
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(GoldLanAnnouncementLayout.FlagsOffset)),
            payload[GoldLanAnnouncementLayout.PlayersOffset], payload[GoldLanAnnouncementLayout.MaximumPlayersOffset], name);
    }
}
