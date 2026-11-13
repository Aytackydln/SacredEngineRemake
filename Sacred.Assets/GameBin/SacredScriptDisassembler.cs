using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Read-only script inspection adapted from the owner-provided editor's
/// script_text.rs. Preserves unrecognized suffixes rather than guessing their length.
/// This is diagnostic text, not an executable script language.</summary>
public static class SacredScriptDisassembler
{
    public static string Format(SacredScriptCommand command)
    {
        var operands = ReadOperands(command);
        return $"{SacredScriptOpcodeNames.GetName(command.Opcode)}" +
               (operands.Count == 0 ? string.Empty : " " + string.Join(" ", operands.Select(value => value.Text)));
    }

    public static IReadOnlyList<SacredScriptDisplayOperand> ReadOperands(SacredScriptCommand command)
    {
        var data = command.Bytes.Span;
        if (data.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != data.Length)
            throw new InvalidDataException("Invalid script instruction header.");
        var result = new List<SacredScriptDisplayOperand>();
        var offset = 4;
        while (offset < data.Length)
        {
            var start = offset;
            var tag = data[offset++];
            var shape = SacredScriptDisplayOperandLayout.GetShape(tag);
            var value = ReadValue(data, ref offset, shape);
            if (value is null)
            {
                var suffix = command.Bytes[start..];
                result.Add(new(start, suffix, $"raw@+0x{start:X}={Convert.ToHexString(suffix.Span)}", true));
                break;
            }
            var name = SacredScriptOperandNames.GetName(tag);
            result.Add(new(start, command.Bytes[start..offset], value.Length == 0 ? name : $"{name}={value}", false));
        }
        return result.AsReadOnly();
    }

    private static string? ReadValue(ReadOnlySpan<byte> data, ref int offset, SacredScriptDisplayOperandShape shape)
    {
        switch (shape)
        {
            case SacredScriptDisplayOperandShape.Flag: return string.Empty;
            case SacredScriptDisplayOperandShape.Byte:
                return offset < data.Length ? Number(data[offset++]) : null;
            case SacredScriptDisplayOperandShape.Word:
                if (data.Length - offset < 2) return null;
                var word = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
                offset += 2;
                return Number(word);
            case SacredScriptDisplayOperandShape.String: return ReadString(data, ref offset);
            case SacredScriptDisplayOperandShape.Integer:
                if (data.Length - offset < 4) return null;
                // Negative values can introduce variable/resource/expression operands in Gold.
                // Keep the complete suffix until that opcode-specific shape is understood.
                var integer = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
                if (integer < 0) return null;
                offset += 4;
                return Number(integer);
            case SacredScriptDisplayOperandShape.Position:
                if (data.Length - offset < 4) return null;
                if (BinaryPrimitives.ReadInt32LittleEndian(data[offset..]) == -2)
                {
                    offset += 4;
                    var position = ReadString(data, ref offset);
                    return position is null ? null : "@" + position;
                }
                return ReadIntegers(data, ref offset, 3);
            case SacredScriptDisplayOperandShape.Pair: return ReadIntegers(data, ref offset, 2);
            case SacredScriptDisplayOperandShape.Triple: return ReadIntegers(data, ref offset, 3);
            case SacredScriptDisplayOperandShape.IntegerString:
                if (data.Length - offset < 4) return null;
                var threshold = BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
                if (threshold < 0) return null;
                offset += 4;
                var text = ReadString(data, ref offset);
                return text is null ? null : Number(threshold) + "," + text;
            default: return null;
        }
    }

    private static string? ReadIntegers(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (data.Length - offset < count * 4) return null;
        var values = new string[count];
        for (var index = 0; index < count; index++, offset += 4)
            values[index] = Number(BinaryPrimitives.ReadInt32LittleEndian(data[offset..]));
        return string.Join(",", values);
    }

    private static string? ReadString(ReadOnlySpan<byte> data, ref int offset)
    {
        var length = data[offset..].IndexOf((byte)0);
        if (length < 0) return null;
        var result = new StringBuilder("\"");
        foreach (var value in data.Slice(offset, length))
        {
            if (value is (byte)'"' or (byte)'\\') result.Append('\\').Append((char)value);
            else if (value is >= 0x20 and <= 0x7E) result.Append((char)value);
            else result.Append("\\x").Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }
        offset += length + 1;
        return result.Append('"').ToString();
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
