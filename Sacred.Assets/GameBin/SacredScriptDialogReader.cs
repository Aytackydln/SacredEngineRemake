using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;

namespace Sacred.Assets.GameBin;

public static class SacredScriptDialogReader
{
    public static IReadOnlyList<SacredScriptDialog> Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) throw new InvalidDataException("Missing DefPos.bin header.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != SacredDefPosVersionedHeaderLayout.FormatMarker)
            return Array.Empty<SacredScriptDialog>(); // The old text/recompile form has no established dialog table here.
        if (data.Length < 8) throw new InvalidDataException("Truncated DefPos.bin header.");
        var positions = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var offset = 8L + positions * (long)SacredDefPosPositionLayout.SerializedSize;
        if (offset + SacredScriptDialogTableHeaderLayout.SerializedSize > data.Length) throw new InvalidDataException("Missing DefPos.bin dialog count.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[(int)offset..]);
        offset += SacredScriptDialogTableHeaderLayout.SerializedSize;
        if (offset + count * (long)SacredScriptDialogLayout.SerializedSize > data.Length)
            throw new InvalidDataException("Truncated DefPos.bin dialog table.");
        var result = new SacredScriptDialog[checked((int)count)];
        for (var index = 0; index < result.Length; index++)
            result[index] = SacredScriptDialog.FromPayload(data.Slice((int)offset + index * SacredScriptDialogLayout.SerializedSize, SacredScriptDialogLayout.SerializedSize));
        return Array.AsReadOnly(result);
    }
}
