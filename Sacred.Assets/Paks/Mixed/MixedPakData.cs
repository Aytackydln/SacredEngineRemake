using System.Text;

namespace Sacred.Assets.Paks.Mixed;

public sealed class MixedPakData
{
    private const int HeaderSize = 0x100;
    private static readonly Encoding NameEncoding = Encoding.ASCII;

    private readonly Dictionary<uint, List<MixedCutoutRecord>> _groups = new();
    private readonly Dictionary<uint, uint> _cutoutIdToGroup = new();

    private MixedPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Mixed.pak is too small to contain a header.");

        var count = PakDataHelpers.ReadEntryCount(data, HeaderSize, PakDataHelpers.EntryDescriptorSize, "Mixed.pak");
        var descriptors = PakDataHelpers.ReadEntryDescriptors(data, HeaderSize, count, "Mixed.pak");
        for (uint mixedId = 0; mixedId < count; mixedId++)
        {
            var descriptor = descriptors[(int)mixedId];
            var offset = descriptor.Offset;
            var size = descriptor.Size;
            if (offset == 0 || size <= 0x10 || offset > int.MaxValue || size > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            var recordSize = (int)size;
            if (recordOffset + recordSize > data.Length)
                continue;

            var pieceCount = Math.Min(
                BitConverter.ToUInt32(data.Slice(recordOffset, 4)),
                (uint)Math.Max(0, (recordSize - 0x10) / 0x40));
            if (pieceCount == 0)
                continue;

            var pieces = new List<MixedCutoutRecord>((int)pieceCount);
            var pieceOffset = recordOffset + 0x10;
            for (uint pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
            {
                if (pieceOffset + 0x40 > data.Length)
                    break;

                var name = PakDataHelpers.ReadCString(data, pieceOffset, 0x20, NameEncoding);
                var rec = pieceOffset + 0x20;
                var piece = new MixedCutoutRecord(
                    mixedId,
                    pieceIndex,
                    name,
                    BitConverter.ToUInt32(data.Slice(rec, 4)),
                    BitConverter.ToUInt16(data.Slice(rec + 0x04, 2)),
                    BitConverter.ToUInt16(data.Slice(rec + 0x06, 2)),
                    BitConverter.ToInt16(data.Slice(rec + 0x08, 2)),
                    BitConverter.ToInt16(data.Slice(rec + 0x0A, 2)),
                    BitConverter.ToSingle(data.Slice(rec + 0x10, 4)),
                    BitConverter.ToSingle(data.Slice(rec + 0x14, 4)),
                    BitConverter.ToSingle(data.Slice(rec + 0x18, 4)),
                    BitConverter.ToSingle(data.Slice(rec + 0x1C, 4)));
                pieces.Add(piece);
                _cutoutIdToGroup.TryAdd(piece.CutoutId, mixedId);
                pieceOffset += 0x40;
            }

            if (pieces.Count > 0)
                _groups[mixedId] = pieces;
        }
    }

    public static MixedPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public IReadOnlyList<MixedCutoutRecord>? GetGroup(uint groupId) =>
        _groups.GetValueOrDefault(groupId);

    public uint? ResolveGroupId(uint referenceId)
    {
        if (_groups.ContainsKey(referenceId))
            return referenceId;

        return _cutoutIdToGroup.TryGetValue(referenceId, out var groupId)
            ? groupId
            : null;
    }
}
