using System.Buffers.Binary;

namespace Sacred.GameServer.Session;

/// <summary>
/// Converts a host's raw world-bank upload (0x1B1..0x1B4) into the form the original server hands to participants (answer to 0x1B5).
/// Only bank 0x1B3 differs: the server writer (0x4D22E7, client reader 0x7E6CA3) sends just the 4-byte runtime word at +0x120 of every
/// 292-byte section-1 record, while the host upload carries the whole record. Bank layout: ten u32 section counts (40 bytes), then
/// section 0 = count0 x [u32 key][u32 n][n x u32], section 1 = count1 x record, followed by the remaining sections unchanged.
/// </summary>
internal static class WorldBankForm
{
    private const int HeaderSize = 40;
    private const int UploadRecordSize = 292;
    private const int ServerWordOffset = 0x120;
    private const int ServerRecordSize = 4;

    /// <summary>Length of the participant form of <paramref name="upload"/>; -1 when the bank needs no conversion
    /// (other banks, dummy or malformed payloads, which are replayed unchanged).</summary>
    public static int GetServedLength(ushort opcode, ReadOnlySpan<byte> upload) =>
        TryLocateSectionOne(opcode, upload, out _, out var count) ? upload.Length - count * (UploadRecordSize - ServerRecordSize) : -1;

    /// <summary>Writes the participant form into <paramref name="destination"/> (at least <see cref="GetServedLength"/> bytes).</summary>
    public static bool TryConvert(ushort opcode, ReadOnlySpan<byte> upload, Span<byte> destination, out int length)
    {
        length = 0;
        if (!TryLocateSectionOne(opcode, upload, out var start, out var count)) return false;
        var served = upload.Length - count * (UploadRecordSize - ServerRecordSize);
        if (destination.Length < served) return false;
        upload[..start].CopyTo(destination);
        var write = start;
        var read = start + ServerWordOffset;
        for (var i = 0; i < count; i++, read += UploadRecordSize, write += ServerRecordSize)
            upload.Slice(read, ServerRecordSize).CopyTo(destination[write..]);
        upload[(start + count * UploadRecordSize)..].CopyTo(destination[write..]);
        length = served;
        return true;
    }

    private static bool TryLocateSectionOne(ushort opcode, ReadOnlySpan<byte> upload, out int start, out int count)
    {
        start = 0;
        count = 0;
        if (opcode != 0x1B3 || upload.Length < HeaderSize) return false;
        var keyed = BinaryPrimitives.ReadUInt32LittleEndian(upload);
        var records = BinaryPrimitives.ReadUInt32LittleEndian(upload[4..]);
        if (records == 0 || records > upload.Length / UploadRecordSize) return false;
        long cursor = HeaderSize;
        for (uint i = 0; i < keyed; i++)
        {
            if (cursor + 8 > upload.Length) return false;
            var words = BinaryPrimitives.ReadUInt32LittleEndian(upload[(int)(cursor + 4)..]);
            cursor += 8 + 4L * words;
        }
        if (cursor + (long)records * UploadRecordSize > upload.Length) return false;
        start = (int)cursor;
        count = (int)records;
        return true;
    }
}
