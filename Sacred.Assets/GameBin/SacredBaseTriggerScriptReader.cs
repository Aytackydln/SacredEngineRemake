using System.Text;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

/// <summary>Literal/resolved SetBaseTrigger operands only. Control flow and callback installation are separate work.</summary>
public static class SacredBaseTriggerScriptReader
{
    public static bool TryRead(SacredScriptCommand command, IReadOnlyDictionary<string, SacredScriptPosition> definitions,
        out SacredBaseTriggerDefinition? trigger)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        trigger = null;
        if (command.Bytes.Length < SacredScriptCommandHeaderLayout.SerializedSize || command.Opcode != SacredScriptPortalOpcodes.SetBaseTrigger) return false;
        var args = SacredPortalScriptArguments.Read(command, definitions);
        if (args?.Name is not { Length: > 0 } name ||
            !args.Positions.TryGetValue((byte)SacredScriptArgumentKind.TriggerFirstCorner, out var first)) return false;
        var last = args.Positions.GetValueOrDefault((byte)SacredScriptArgumentKind.TriggerLastCorner, first);
        trigger = new(Encoding.Latin1.GetBytes(name), first, last) { SourceOffset = command.FileOffset };
        return true;
    }
}
