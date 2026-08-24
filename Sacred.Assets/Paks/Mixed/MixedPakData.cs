using System.Runtime.InteropServices;
using System.Text;
using Sacred.Core.Pak.Mixed;

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
            if (offset == 0 || size <= MixedPakGroupLayout.SerializedSize || offset > int.MaxValue || size > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            var recordSize = (int)size;
            if (recordOffset + recordSize > data.Length)
                continue;

            var header = MemoryMarshal.Read<MixedPakGroupLayout>(
                data.Slice(recordOffset, MixedPakGroupLayout.SerializedSize));
            var pieceCount = Math.Min(
                header.PieceCount,
                (uint)Math.Max(0, (recordSize - MixedPakGroupLayout.SerializedSize) / MixedPakPieceLayout.SerializedSize));
            if (pieceCount == 0)
                continue;

            var pieces = new List<MixedCutoutRecord>((int)pieceCount);
            var pieceOffset = recordOffset + MixedPakGroupLayout.SerializedSize;
            for (uint pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
            {
                if (pieceOffset + MixedPakPieceLayout.SerializedSize > data.Length)
                    break;

                var name = PakDataHelpers.ReadCString(data, pieceOffset, 0x20, NameEncoding);
                var layout = MemoryMarshal.Read<MixedPakPieceLayout>(
                    data.Slice(pieceOffset, MixedPakPieceLayout.SerializedSize));
                var piece = new MixedCutoutRecord(name,
                    layout.CutoutId,
                    layout.Right,
                    layout.Bottom,
                    layout.Left,
                    layout.Top,
                    layout.Uv0,
                    layout.Uv1,
                    layout.Uv2,
                    layout.Uv3);
                pieces.Add(piece);
                _cutoutIdToGroup.TryAdd(piece.CutoutId, mixedId);
                pieceOffset += MixedPakPieceLayout.SerializedSize;
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
