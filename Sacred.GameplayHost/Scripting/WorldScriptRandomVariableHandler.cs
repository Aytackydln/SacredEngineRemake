using System.Text;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x49D450 startup random variable. Range parsing uses both the
/// signed first-bound sentinel and the recognized operand count. Complete live
/// notification production remains separate from this startup-only caller.</summary>
internal static class WorldScriptRandomVariableHandler
{
    public static void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (!context.IsExecutingStartup)
            throw new InvalidDataException("Live random variables require their native update publication.");
        var name = "";
        var lower = -1;
        var upper = -1;
        var count = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            switch (operand.Kind)
            {
                case SacredScriptArgumentKind.NullTerminatedString:
                    var text = WorldScriptDefinitionHandlers.Text(operand);
                    if (text.Contains('+'))
                        throw new InvalidDataException("Random-variable name expansion needs its native normalization/caller comparison.");
                    if (count == 0) name = text;
                    else if (context.Variables.Find(text) is { } value)
                    {
                        if (count == 1) lower = value;
                        else upper = value;
                    }
                    count++;
                    break;
                case SacredScriptArgumentKind.Integer:
                    var integer = WorldScriptDefinitionHandlers.Integer(context, operand);
                    if (lower < 0) lower = integer;
                    else upper = integer;
                    count++;
                    break;
                default:
                    throw new InvalidDataException($"Random-variable operand 0x{(byte)operand.Kind:X2} needs native decoder side-effect comparison.");
            }
        }
        if (name.Length == 0) return;
        // Validate the compared name storage before consuming a shared draw.
        _ = GoldScriptVariableRecord.Create(Encoding.Latin1.GetBytes(name), 0);
        if (name.Any(character => character > byte.MaxValue))
            throw new InvalidDataException("Random-variable names require original Latin-1 bytes.");
        if (lower < 0) lower = 0;
        if (upper < 0) upper = 1;
        if (upper < lower) (lower, upper) = (upper, lower);
        var selected = lower;
        if (upper != lower)
        {
            var random = context.Random ?? throw new InvalidDataException("Random variable requires the shared native RNG.");
            // Native IDIV uses the wrapped signed range width, including 0..Int32.MaxValue.
            selected = unchecked(lower + random() % unchecked(upper - lower + 1));
        }
        context.VariableGlobals.RecordRandom(name, selected);
        context.Variables.Set(name, selected);
    }
}
