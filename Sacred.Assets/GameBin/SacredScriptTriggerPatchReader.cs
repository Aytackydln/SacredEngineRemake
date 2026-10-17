using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.GameBin;

public static class SacredScriptTriggerPatchReader
{
    public static SacredScriptTriggerPatch? Read(SacredScriptCommand command)
    {
        if (command.Opcode != SacredScriptTriggerOpcodes.TriggerPatch) throw new ArgumentException("Expected TriggerPatch.", nameof(command));
        var name = SacredScriptTriggerOpcodes.NamePrefix;
        var cells = new List<SacredScriptPosition>();
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString) name += operand.Text;
            else if (operand.Kind == SacredScriptArgumentKind.TriggerPatchPosition && cells.Count < SacredScriptTriggerOpcodes.MaximumPatchCells)
                cells.Add(operand.Position!.Value);
        }
        return name.Length == SacredScriptTriggerOpcodes.NamePrefix.Length || cells.Count == 0 ? null : new(name, cells.AsReadOnly(), command.FileOffset);
    }
}
