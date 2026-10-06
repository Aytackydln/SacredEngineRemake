using System.Runtime.InteropServices;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

public static class SacredMapAnnotationReader
{
    public static IReadOnlyList<SacredRegionNpcIconLayout> ReadNpcs(ReadOnlySpan<byte> data)
    {
        if (data.Length % SacredRegionNpcIconLayout.SerializedSize != 0)
            throw new InvalidDataException("merc.bin ends inside an NPC map-icon record.");
        return MemoryMarshal.Cast<byte, SacredRegionNpcIconLayout>(data).ToArray();
    }

    public static bool TryReadLabel(SacredScriptCommand command, out SacredMapLabel label)
    {
        label = default;
        var data = command.Bytes.Span;
        const int header = SacredScriptCommandHeaderLayout.SerializedSize;
        if (command.Opcode != SacredScriptPortalOpcodes.SetMapIcon ||
            data.Length <= header + SacredMapLabelArgumentsLayout.SerializedSize)
            return false;
        var args = MemoryMarshal.Read<SacredMapLabelArgumentsLayout>(data[header..]);
        if (args.Position.XKind != SacredScriptArgumentKind.Integer ||
            args.Position.YKind != SacredScriptArgumentKind.Integer ||
            args.Position.ThirdKind != SacredScriptArgumentKind.Integer ||
            args.AngleKind != SacredScriptArgumentKind.Integer ||
            args.ResourceKind != SacredScriptArgumentKind.NullTerminatedString)
            return false;
        var name = data[(header + SacredMapLabelArgumentsLayout.SerializedSize)..];
        if (name[^1] != 0 || name[..^1].Contains((byte)0)) return false;
        label = new(args.Position.TileX, args.Position.TileY,
            args.Position.IconTypeOrLabelCurvature, args.LabelAngleDegrees, Encoding.Latin1.GetString(name[..^1]));
        return true;
    }
}

public readonly record struct SacredMapLabel(int TileX, int TileY, int Curvature, int AngleDegrees, string Resource);
