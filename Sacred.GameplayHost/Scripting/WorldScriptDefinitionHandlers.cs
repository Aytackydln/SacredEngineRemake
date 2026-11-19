using System.Text;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Native definitions, variables and movement callback registration; object creation has a separate handler boundary.</summary>
internal static class WorldScriptDefinitionHandlers
{
    public static bool Execute(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        switch (command.Opcode)
        {
            case SacredScriptRegionLayout.SetOpcode:
            case SacredScriptRegionLayout.RemoveOpcode:
                (context.ScriptRegions ?? throw new InvalidDataException("SetRG requires original sector/resource services."))
                    .Apply(command,context.IsExecutingStartup,context.Multiplayer);
                return true;
            case SacredScriptExecutionOpcodes.StartPosition:
                _ = SacredScriptOperandReader.Read(command);
                if (!context.Multiplayer) throw new NotSupportedException("StartPosition requires its native single-player hero teleport lifecycle.");
                return true;
            case SacredScriptExecutionOpcodes.RandomVariable:
                WorldScriptRandomVariableHandler.Apply(context, command);
                return true;
            case SacredScriptExecutionOpcodes.SetVariableBits:
                WorldScriptBitVariableHandler.Apply(context, command);
                return true;
            case SacredScriptExecutionOpcodes.ClearVariableBits:
                WorldScriptEnteredVariableBits.Apply(context,command);
                return true;
            case SacredScriptExecutionOpcodes.AssociateDialog:
                context.DialogAssociations.Apply(context,command);
                return true;
            case SacredScriptExecutionOpcodes.CreateHideouts:
                if (context.Hideouts is null) throw new InvalidDataException("Hideout generation requires native resource and sector services.");
                context.Hideouts.Apply(context,command);
                return true;
            case WorldScriptMouseovers.Opcode:
                if (context.Mouseovers is null) throw new InvalidDataException("Mouseover registration requires original resources and startup terrain.");
                context.Mouseovers.Apply(context, command);
                return true;
            case SacredScriptPortalOpcodes.SetMapIcon:
                if (context.MapIcons is null) throw new InvalidDataException("SetMapIcon requires original resource lookup and world sector mapping.");
                context.MapIcons.Apply(command, context.IsExecutingStartup);
                return true;
            case SacredScriptExecutionOpcodes.SetStartupParameters:
                var values = SacredScriptOperandReader.Read(command).Where(operand => operand.Kind == SacredScriptArgumentKind.Integer)
                    .Select(operand => Integer(context, operand)).ToArray();
                context.StartupParameters.Set(values);
                return true;
            case SacredScriptRegistrationLayout.Opcode:
                context.Registrations.Append(command);
                return true;
            case SacredScriptTriggerOpcodes.SetTrigger:
                if (SacredScriptTriggerCreationReader.Read(command) is { } creation)
                {
                    if (creation.Name.Contains('$') || creation.Name.StartsWith(SacredScriptTriggerOpcodes.NamePrefix + "res:", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Trigger '{creation.Name}' requires native name resolution.");
                    if (context.WorldTriggers is null)
                        throw new InvalidDataException($"World-trigger allocation/registration is required for '{creation.Name}' (scope {creation.Scope}, state 0x{creation.State:X4}); load the original trigger table and use granted 0x154 IDs.");
                    if (!context.IsExecutingStartup && !context.IsInitializingSector)
                        throw new NotSupportedException("SetTrigger publication requires its compared sector-initialization scope.");
                    context.WorldTriggers.Create(creation, context.IsExecutingStartup ? null :
                        checked((byte)(context.LocalPlayerSlot ?? throw new InvalidDataException("Sector trigger publication needs the actual owner."))));
                    // Gold 0x49655B clears the live name buffer across the following
                    // variable suffix. The supported registry limits names to 63 bytes.
                    if (creation.Name.Length < 64) WorldScriptVariableCallerEffects.EstablishZero(context);
                }
                return true;
            case SacredScriptTriggerOpcodes.TriggerPatch:
                if (SacredScriptTriggerPatchReader.Read(command) is { } patch)
                {
                    if (patch.Name.Contains('$') || patch.Name.StartsWith(SacredScriptTriggerOpcodes.NamePrefix + "res:", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Trigger '{patch.Name}' requires native name resolution.");
                    if (context.WorldTriggers is null)
                        throw new InvalidDataException("TriggerPatch requires world registration and the appropriate startup/live tile mutation handler.");
                    if (context.IsExecutingStartup) context.WorldTriggers.Patch(patch, null);
                    else
                    {
                        if (!context.IsInitializingSector || context.WorldTriggers is not WorldScriptTriggerRegistry registry ||
                            context.CreatureWorld is not WorldScriptCreatureWorld { Terrain: { } terrain })
                            throw new NotSupportedException("TriggerPatch requires its bound sector-init terrain/publication services.");
                        registry.PatchAndPublish(patch,checked((byte)(context.LocalPlayerSlot ??
                            throw new InvalidDataException("Sector trigger patch needs the actual owner."))), cell => terrain.MarkInitializingTrigger(context,cell));
                    }
                }
                return true;
            case SacredScriptExecutionOpcodes.SetVariable:
            case SacredScriptExecutionOpcodes.DefineVariable:
            case SacredScriptExecutionOpcodes.AddVariable:
                Variable(context, command);
                return true;
            case SacredScriptExecutionOpcodes.DefinePosition:
            case SacredScriptExecutionOpcodes.DefinePoolPosition:
                Definition(context, command);
                return true;
            case SacredScriptPortalOpcodes.SetBaseTrigger:
                BaseTrigger(context, command);
                return true;
            case SacredScriptPortalOpcodes.OnMoveOver:
                MovementCallback(context, command);
                return true;
            default:
                return false;
        }
    }

    internal static string Text(SacredScriptOperand operand)
    {
        var name = operand.Text ?? throw new InvalidDataException($"Expected name at +0x{operand.Offset:X}.");
        if (name.Contains('$') || name.Contains("+Var(",StringComparison.Ordinal) || name.StartsWith("res:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Name '{name}' requires the native string/resource resolver at +0x{operand.Offset:X}.");
        return name;
    }
    internal static int Integer(WorldScriptExecutionContext context, SacredScriptOperand operand) =>
        operand.Integer ?? context.Variables.Find(Text(operand)) ??
        throw new InvalidDataException($"Unresolved integer variable '{operand.Text}' at +0x{operand.Offset:X}.");

    private static string TextForVariable(SacredScriptOperand operand)
    {
        var text = operand.Text ?? throw new InvalidDataException("Expected variable name.");
        if (text.Contains('$') || text.StartsWith("res:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Variable '{text}' requires resource resolution.");
        return text;
    }

    private static void Variable(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        string? name = null;
        int? value = null;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                if (name is null or "") name = WorldScriptVariableNames.Expand(TextForVariable(operand), context.Variables.Find);
                else value = context.Variables.Find(Text(operand)) ?? throw new InvalidDataException($"Unresolved source variable '{operand.Text}'.");
            }
            else if (operand.Kind == SacredScriptArgumentKind.Integer) value = Integer(context, operand);
            // Native handlers ignore successfully decoded operands outside their cases.
        }
        if (string.IsNullOrEmpty(name)) return;
        if (value is null) throw new InvalidDataException($"Variable '{name}' has no initialized value operand.");
        if (command.Opcode == SacredScriptExecutionOpcodes.AddVariable) context.Variables.Add(name, value.Value);
        else context.Variables.Set(name, value.Value);
        context.VariableCallerIndex = context.VariableGlobals.RecordWithCallerIndex(name, value.Value,
            command.Opcode == SacredScriptExecutionOpcodes.AddVariable, context.VariableCallerIndex);
        if (name.Length >= 7)
            context.VariableCallerStopsImmediately = WorldScriptVariableCallerEffects.StopsImmediately(name.Length == 7 ? '\0' : name[7]);
    }

    private static void Definition(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        string? name = null;
        var parameters = new[] { -1, -1, -1, -1 };
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                if (name is null or "") name = Text(operand);
                else
                {
                    var copy = context.Definitions.Find(WorldDefinitionKind.Position, Text(operand)) ??
                        throw new InvalidDataException($"Unresolved source position '{operand.Text}'.");
                    for (var i = 0; i < 3; i++) parameters[i] = copy.Parameter(i);
                }
            }
            else if (operand.Kind == SacredScriptArgumentKind.Integer)
            {
                var index = Array.IndexOf(parameters, -1);
                if (index >= 0) parameters[index] = Integer(context, operand);
            }
        }
        if (name is null) return;
        if (command.Opcode == SacredScriptExecutionOpcodes.DefinePosition)
        {
            context.Definitions.DefinePosition(name, parameters[0], parameters[1], parameters[2], parameters[3]);
            // Gold 0x4787C3/0x478812 writes the first Int32 parameter over the
            // following variable caller's +4..+7. Only a proven nonnumeric +7
            // byte closes atoi without needing the following scratch bytes.
            var operands = SacredScriptOperandReader.Read(command);
            if (name.Length < 64 && operands.Count > 0 &&
                operands[0].Kind == SacredScriptArgumentKind.NullTerminatedString &&
                operands.Skip(1).All(operand => operand.Kind == SacredScriptArgumentKind.Integer && operand.Integer is not null))
            {
                var first = unchecked((byte)(parameters[0] >> 24));
                if (first is not (>= (byte)'0' and <= (byte)'9') && first is not ((byte)'+' or (byte)'-' or (byte)' ') &&
                    first is not (>= 9 and <= 13)) WorldScriptVariableCallerEffects.EstablishZero(context);
            }
        }
        else context.Definitions.AppendPoolPosition(name, parameters[0], parameters[1], parameters[2], parameters[3]);
    }

    private static void BaseTrigger(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        string? name = null;
        SacredScriptPosition? first = null, last = null;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString) name = Text(operand);
            else if (operand.Kind is SacredScriptArgumentKind.TilePosition or SacredScriptArgumentKind.TriggerFirstCorner)
            {
                first = Position(operand);
                last ??= first;
            }
            else if (operand.Kind == SacredScriptArgumentKind.TriggerLastCorner)
            {
                last = Position(operand);
                first ??= new(last.Value.X, last.Value.Y, 0);
            }
        }
        if (string.IsNullOrEmpty(name) || first is null) return;
        context.AddTrigger(new(Encoding.Latin1.GetBytes(name), first.Value, last ?? first.Value) { SourceOffset = command.FileOffset });
        SacredScriptPosition Position(SacredScriptOperand operand) => operand.Position ??
            context.Definitions.ResolvePosition(Text(operand), context.Random, context.PositionAllowed);
    }

    private static void MovementCallback(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        string? name = null;
        int? function = null;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString) name = Text(operand);
            else if (operand.Kind == SacredScriptArgumentKind.Integer) function = Integer(context, operand);
        }
        if (string.IsNullOrEmpty(name) || function is null) return;
        context.RegisterMovement(name, function.Value);
        // Gold OnMoveOver 0x4918DF clears 128 name bytes. Its byte 99 aliases
        // the next variable caller's suffix; no supplied name may overwrite it.
        if (SacredScriptOperandReader.Read(command)
            .Where(operand => operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            .All(operand => operand.Text!.Length < 100)) WorldScriptVariableCallerEffects.EstablishZero(context);
    }
}
