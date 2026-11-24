using Sacred.Assets.Executable;
using Sacred.Assets.GameBin;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Networking;

/// <summary>One first-instruction Enter callback, distinct from the complete
/// native EnterSector operation and its post-callback population/patch work.</summary>
public sealed class WorldHostSectorEntry
{
    private readonly WorldParticipantPopulationCommand _population;
    public bool IsCompleted { get; private set; }
    public string? Blocker { get; private set; }
    public ushort? FunctionIndex { get; private set; }
    public int PopulationCommands => _population.ExecutedCommands;
    public int ClearedSchedules => _population.ClearedSchedules;
    public IReadOnlyList<GoldMessage> Publications { get; private set; } = [];
    public int SentPublications { get; internal set; }

    internal WorldHostSectorEntry(string installation) => _population = new(SacredPopulationDefinitions.Load(installation),
        PopulationBalanceBinReader.Load(Path.Combine(installation, "bin", "Balance.bin")));

    internal bool TryRun(WorldScriptExecutionContext context, WorldScriptSectorEnvironment environment,
        uint setting, Func<string, uint?> bindings, CancellationToken token)
    {
        if (IsCompleted) return true;
        token.ThrowIfCancellationRequested();
        Blocker = null;
        FunctionIndex = WorldScriptSectorEntryCallback.Select(context, environment, bindings);
        if (FunctionIndex is { } index && index > 0 && index < context.Functions.Count)
        {
            var function = context.Functions[index];
            foreach (var command in SacredCompiledScriptReader.Read(context.FunctionCode.Slice(function.Start, function.ByteLength)))
            {
                try
                {
                    if (command.Opcode is SacredScriptControlOpcodes.Stop or SacredScriptControlOpcodes.Nop) continue;
                    if (command.Opcode == SacredScriptExecutionOpcodes.SetSectorMonsterParameters &&
                        SacredScriptOperandReader.Read(command).All(value => value.Kind == SacredScriptArgumentKind.Integer && value.Integer is not null)) continue;
                    if (command.Opcode == SacredScriptSpawnEquipmentLayout.Opcode) { _ = SacredScriptSpawnEquipment.Read(command); continue; }
                    if (command.Opcode == SacredScriptCreateCreature.Opcode && context.CreatureWorld is WorldScriptCreatureWorld creatures)
                    {
                        if (!WorldScriptEnteredCreatureGate.CanSkip(context, command, setting, creatures.Resources, creatures.ResourceText))
                            creatures.PrepareEnteredCreature(context, command, environment);
                        continue;
                    }
                    if (command.Opcode == SacredScriptPopulation.Opcode && _population.SupportsFilteredEmpty(command, setting, environment)) continue;
                    if (command.Opcode == 8 && context.ObjectWorld is WorldScriptObjectWorld objects)
                    { objects.PrepareEntered(context, command, environment); continue; }
                }
                catch (Exception error) when (error is InvalidDataException or NotSupportedException)
                { Blocker = error.Message; }
                Blocker = $"Sector Enter '{function.Name}' needs opcode {command.Opcode} at 0x{function.Start + command.FileOffset:X}; no body executed. {Blocker}";
                return false;
            }
        }
        try
        {
            var creatures = context.CreatureWorld as WorldScriptCreatureWorld;
            var effects = creatures?.Effects;
            var priorCreatures = creatures?.Prepared.Count ?? 0;
            var priorEffects = effects?.Created.Count ?? 0;
            IsCompleted = WorldScriptSectorEntryCallback.TryInvoke(context, environment, setting, FunctionIndex, selected =>
            {
                if (selected == 0 || selected >= context.Functions.Count) return true;
                new WorldScriptExecutor(context, (state, command) =>
                {
                    if (command.Opcode != SacredScriptPopulation.Opcode) return false;
                    _population.Execute(state, command, setting); return true;
                }).Invoke(selected, token);
                return true;
            });
            if (IsCompleted)
            {
                var creaturePublications = creatures?.Prepared.Skip(priorCreatures)
                    .Select(value => (Offset: value.Blueprint.SourceOffset, Messages: value.Publications)) ?? [];
                var effectPublications = effects?.Created.Skip(priorEffects)
                    .Select(value => (Offset: value.SourceOffset, Messages: (IReadOnlyList<GoldMessage>)[value.Publication])) ?? [];
                Publications = creaturePublications.Concat(effectPublications).OrderBy(value => value.Offset)
                    .SelectMany(value => value.Messages).ToArray();
            }
            return IsCompleted;
        }
        catch { context.IsInvalidated = true; throw; }
    }
}
