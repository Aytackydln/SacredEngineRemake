using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Decodes the recovered literal subset of opcode 8. An unsupported operand
/// rejects the whole instruction; callers must not use partial results as placements.</summary>
public static partial class SacredScriptCreateObjectReader
{
    public static bool TryRead(SacredScriptCommand command,
        [NotNullWhen(true)] out SacredScriptCreateObject? creation, out string? diagnostic)
    {
        creation = null;
        diagnostic = null;
        var data = command.Bytes.Span;
        if (data.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != data.Length)
            return Fail("Invalid instruction header.", out diagnostic);
        if (command.Opcode != SacredScriptCommandHeaderLayout.CreateObjectOpcode)
            return Fail("Not a create-object instruction.", out diagnostic);

        string? name = null;
        uint? typeId = null;
        SacredScriptPosition? tile = null, world = null;
        string? symbolicTilePosition = null;
        short? height = null;
        var offset = 4;
        while (offset < data.Length)
        {
            var tag = (SacredScriptArgumentKind)data[offset];
            var payload = data[(offset + 1)..];
            switch (tag)
            {
                case SacredScriptArgumentKind.NullTerminatedString:
                case SacredScriptArgumentKind.ObjectReference:
                    var end = payload.IndexOf((byte)0);
                    if (end < 0) return Fail($"Unterminated string at +0x{offset:X}.", out diagnostic);
                    // Byte-preserving display decoding, as used for other original script names.
                    name = Encoding.Latin1.GetString(payload[..end]);
                    offset += end + 2;
                    break;
                case SacredScriptArgumentKind.TypeId:
                    if (payload.Length < 4) return Truncated(offset, out diagnostic);
                    typeId = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                    offset += SacredScriptTypeArgumentLayout.SerializedSize;
                    break;
                case SacredScriptArgumentKind.HeightOffset:
                    if (payload.Length < 4) return Truncated(offset, out diagnostic);
                    height = BinaryPrimitives.ReadInt16LittleEndian(payload);
                    offset += SacredScriptHeightArgumentLayout.SerializedSize;
                    break;
                case SacredScriptArgumentKind.TilePosition:
                case SacredScriptArgumentKind.WorldPosition:
                    if (payload.Length < 4) return Truncated(offset, out diagnostic);
                    var x = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    if (tag == SacredScriptArgumentKind.TilePosition && x == -2)
                    {
                        var symbolicName = payload[4..].IndexOf((byte)0);
                        if (symbolicName < 0)
                            return Fail($"Unterminated symbolic tile position at +0x{offset:X}.", out diagnostic);
                        symbolicTilePosition = Encoding.Latin1.GetString(payload.Slice(4, symbolicName));
                        offset += symbolicName + 6;
                        break;
                    }
                    if (payload.Length < 12) return Truncated(offset, out diagnostic);
                    var position = new SacredScriptPosition(x,
                        BinaryPrimitives.ReadInt32LittleEndian(payload[4..]),
                        BinaryPrimitives.ReadInt32LittleEndian(payload[8..]));
                    if (tag == SacredScriptArgumentKind.TilePosition) tile = position;
                    else world = position;
                    offset += SacredScriptPositionArgumentLayout.SerializedSize;
                    break;
                default:
                    return Fail($"Unsupported operand 0x{(byte)tag:X2} at +0x{offset:X}.", out diagnostic);
            }
        }

        if (!typeId.HasValue) return Fail("No literal type identifier.", out diagnostic);
        if (tile is null && world is null && symbolicTilePosition is not null)
            return Fail("Symbolic tile position requires a DefPos resolver.", out diagnostic);
        creation = new SacredScriptCreateObject(name, typeId.Value, tile, world, height, symbolicTilePosition);
        return true;
    }

    private static bool Truncated(int offset, out string? diagnostic) =>
        Fail($"Truncated operand at +0x{offset:X}.", out diagnostic);

    private static bool Fail(string message, out string? diagnostic)
    {
        diagnostic = message;
        return false;
    }
}
public static partial class SacredScriptCreateObjectReader
{
    /// <summary>Recovers the literal leading operands from an object creation even when its
    /// later script-specific operands are not decoded yet.</summary>
    public static bool TryReadLiteralPlacement(SacredScriptCommand command,
        [NotNullWhen(true)] out SacredScriptCreateObject? creation)
    {
        creation = null;
        var data = command.Bytes.Span;
        if (data.Length < 4 || command.Opcode != SacredScriptCommandHeaderLayout.CreateObjectOpcode ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != data.Length)
            return false;

        uint? typeId = null;
        SacredScriptPosition? tile = null, world = null;
        string? symbolicTilePosition = null;
        var offset = 4;
        while (offset < data.Length)
        {
            var tag = (SacredScriptArgumentKind)data[offset];
            var payload = data[(offset + 1)..];
            switch (tag)
            {
                case SacredScriptArgumentKind.TypeId when payload.Length >= 4:
                    typeId = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                    offset += SacredScriptTypeArgumentLayout.SerializedSize;
                    continue;
                case SacredScriptArgumentKind.NullTerminatedString or SacredScriptArgumentKind.ObjectReference:
                    var end = payload.IndexOf((byte)0);
                    if (end < 0)
                        return false;
                    offset += end + 2;
                    continue;
                case SacredScriptArgumentKind.TilePosition or SacredScriptArgumentKind.WorldPosition when payload.Length >= 12:
                    var x = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    if (tag == SacredScriptArgumentKind.TilePosition && x == -2)
                    {
                        var symbolicName = payload[4..].IndexOf((byte)0);
                        if (symbolicName < 0)
                            return false;
                        symbolicTilePosition = Encoding.Latin1.GetString(payload.Slice(4, symbolicName));
                        offset += symbolicName + 6;
                        continue;
                    }
                    var position = new SacredScriptPosition(x,
                        BinaryPrimitives.ReadInt32LittleEndian(payload[4..]),
                        BinaryPrimitives.ReadInt32LittleEndian(payload[8..]));
                    if (tag == SacredScriptArgumentKind.TilePosition) tile = position;
                    else world = position;
                    offset += SacredScriptPositionArgumentLayout.SerializedSize;
                    continue;
                default:
                    offset = data.Length;
                    break;
            }
        }

        if (!typeId.HasValue || (tile is null && world is null && symbolicTilePosition is null))
            return false;
        creation = new SacredScriptCreateObject(null, typeId.Value, tile, world, null, symbolicTilePosition);
        return true;
    }
}
