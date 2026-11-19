using System.Buffers.Binary;
using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Verified literal predicates used by NetScriptCamp startup. The variable callback supplies
/// lookup and resource-name resolution; other predicate shapes remain caller responsibilities.</summary>
public static class SacredScriptConditionReader
{
    public static bool? Evaluate(SacredScriptCommand command, bool multiplayer, Func<string, int?> variable)
    {
        ArgumentNullException.ThrowIfNull(variable);
        var data = command.Bytes.Span;
        if (data.Length < 5 || BinaryPrimitives.ReadUInt16LittleEndian(data[2..]) != data.Length) return null;
        if (command.Opcode is not (SacredScriptControlOpcodes.If or SacredScriptControlOpcodes.ElseIf)) return null;
        var operands = data[4..];
        if (operands.Length == 1 && operands[0] == SacredScriptConditionTags.MultiplayerActive) return multiplayer;
        if (operands.Length == 1 && operands[0] == SacredScriptConditionTags.MultiplayerInactive) return !multiplayer;
        if (operands.Length > 0 && operands[0] == SacredScriptVariableBitConditionLayout.SetTag)
            return BitClauses(operands, variable);
        if (operands.Length < 6 || operands[0] is not (SacredScriptVariableComparisonLayout.EqualTag or SacredScriptVariableComparisonLayout.GreaterThanTag or SacredScriptVariableComparisonLayout.LessThanTag)) return null;
        var threshold = BinaryPrimitives.ReadInt32LittleEndian(operands[1..]);
        if (threshold < 0 || operands[5..].IndexOf((byte)0) != operands.Length - 6) return null;
        var name = Encoding.Latin1.GetString(operands[5..^1]);
        // Native missing-variable lookup fails the predicate, rather than supplying zero.
        return variable(name) is { } value && (operands[0] switch
        {
            SacredScriptVariableComparisonLayout.EqualTag => value == threshold,
            SacredScriptVariableComparisonLayout.GreaterThanTag => value > threshold,
            _ => value < threshold
        });
    }

    private static bool? BitClauses(ReadOnlySpan<byte> bytes, Func<string, int?> variable)
    {
        var result = true;
        while (!bytes.IsEmpty)
        {
            if (bytes.Length < 6 || bytes[0] is not (SacredScriptVariableComparisonLayout.EqualTag or SacredScriptVariableBitConditionLayout.SetTag)) return null;
            var operand = BinaryPrimitives.ReadInt32LittleEndian(bytes[SacredScriptVariableBitConditionLayout.IndexOffset..]);
            var end = bytes[SacredScriptVariableBitConditionLayout.NameOffset..].IndexOf((byte)0);
            if (operand < 0 || bytes[0] == SacredScriptVariableBitConditionLayout.SetTag && operand > 31 || end < 1) return null;
            var name = Encoding.Latin1.GetString(bytes.Slice(SacredScriptVariableBitConditionLayout.NameOffset, end));
            if (name.Contains('$') || name.Contains('+') || name.Contains(':') || string.Equals(name, "HeroQBit", StringComparison.OrdinalIgnoreCase)) return null;
            if (variable(name) is not { } value) return null;
            result &= bytes[0] == SacredScriptVariableBitConditionLayout.SetTag ? (value & (1 << operand)) != 0 : value == operand;
            bytes = bytes[(SacredScriptVariableBitConditionLayout.NameOffset + end + 1)..];
        }
        return result;
    }
}
