using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>The reached entered-sector opcode 8 caller. Gold gives its literal
/// world position precedence over tile coordinates. Other operand shapes retain
/// their existing guards rather than broadening startup construction.</summary>
internal static class WorldScriptEnteredFixedEffect
{
    public static SacredScriptCreateObject Read(SacredScriptCommand command)
    {
        var operands = SacredScriptOperandReader.Read(command);
        if (command.Opcode != 8 || operands.Count != 5 ||
            operands[0] is not { Kind: SacredScriptArgumentKind.NullTerminatedString, Text: { } name } ||
            !WorldScriptNameComparer.Instance.Equals(name, "NON_UNIQUE") ||
            operands[1] is not { Kind: SacredScriptArgumentKind.TypeId, Integer: > 0 } type ||
            operands[2] is not { Kind: SacredScriptArgumentKind.TilePosition, Position: { X: >= 0, Y: >= 0, Z: 0 } tile } ||
            operands[3] is not { Kind: SacredScriptArgumentKind.WorldPosition, Position: { X: >= 0, Y: >= 0, Z: 0 } world } ||
            operands[4] is not { Kind: SacredScriptArgumentKind.HeightOffset, Integer: 0 })
            throw new NotSupportedException("Entered CreateObj requires its compared unnamed fixed Effect with literal tile/world position and zero height.");
        return new(name, checked((uint)type.Integer!.Value), tile, world, 0);
    }
}
