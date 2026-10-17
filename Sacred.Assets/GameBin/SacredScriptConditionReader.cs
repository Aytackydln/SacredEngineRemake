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
        if (operands.Length == 1 && operands[0] == SacredScriptConditionTags.MultiplayerInactive) return !multiplayer;
        if (operands.Length < 6 || operands[0] != SacredScriptVariableComparisonLayout.GreaterThanTag) return null;
        var threshold = BinaryPrimitives.ReadInt32LittleEndian(operands[1..]);
        if (threshold < 0 || operands[5..].IndexOf((byte)0) != operands.Length - 6) return null;
        var name = Encoding.Latin1.GetString(operands[5..^1]);
        // Native missing-variable lookup fails the predicate, rather than supplying zero.
        return variable(name) is { } value && value > threshold;
    }
}
