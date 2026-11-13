using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x49B840 ordinary variable bit-set. Actor/hero bit families retain their own required boundary.</summary>
internal static class WorldScriptBitVariableHandler
{
    public static void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (!context.IsExecutingStartup) throw new InvalidDataException("Live variable bits require native update publication.");
        string? name = null; var mask = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                var text = operand.Text!;
                if (name is null or "") name = WorldScriptVariableNames.Expand(text, context.Variables.Find);
                else
                {
                    if (text.StartsWith("res",StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Variable bits require the resource-associated actor handler.");
                    if (context.Variables.Find(text) is { } bit) mask |= 1 << bit;
                }
            }
            else if (operand.Kind == SacredScriptArgumentKind.Integer)
            {
                var bit = operand.Integer ?? context.Variables.Find(operand.Text!);
                if (bit is null) throw new InvalidDataException("Variable bit index requires native expression resolution.");
                mask |= 1 << bit.Value;
            }
            else throw new InvalidDataException($"Variable bit operand 0x{(byte)operand.Kind:X2} requires native decoding.");
        }
        if (string.IsNullOrEmpty(name)) return;
        if (WorldScriptNameComparer.Instance.Equals(name,"HeroQBit")) throw new InvalidDataException("HeroQBit requires the native active hero.");
        if (name.Contains('$') || name.StartsWith("res:",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Variable bit name requires native resource resolution.");
        context.Variables.Set(name,(context.Variables.Find(name) ?? 0) | mask);
        // Native 0x49B874..0x49B8A6 clears the overlapping +7 suffix of the next
        // SetVar/AddVar caller in the same dispatcher frame.
        WorldScriptVariableCallerEffects.EstablishZero(context);
    }
}
