using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.World.Objects;

internal sealed class WorldDoorScriptReader
{
    private readonly Dictionary<string, Trigger> _triggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (bool? Open, bool? Locked)> _objectStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint _sourcePrefix;

    public WorldDoorScriptReader(IReadOnlyList<SacredScriptCommand> commands, uint sourcePrefix, IReadOnlyList<SacredScriptCommand> startup)
    {
        _sourcePrefix = sourcePrefix;
        foreach (var command in commands)
        {
            if (command.Opcode is not (SacredScriptDoorOpcodes.CreateTrigger or
                SacredScriptDoorOpcodes.TriggerPatch)) continue;
            var args = SacredScriptDoorArguments.Read(command);
            if (args?.Name is not { } name) continue;
            switch (command.Opcode)
            {
                case SacredScriptDoorOpcodes.CreateTrigger:
                    // Keep trigger identity; linked models must share a single state.
                    _triggers[name] = new(sourcePrefix + (uint)command.FileOffset,
                        args.Open ?? false, args.Locked ?? false,
                        args.TriggerType is not null && (args.Open.HasValue || args.Locked.HasValue));
                    break;
                case SacredScriptDoorOpcodes.TriggerPatch:
                    if (_triggers.TryGetValue(name, out var trigger)) trigger.Cells.AddRange(args.Cells);
                    break;
            }
        }
        var conditionalDepth = 0;
        foreach (var command in startup)
        {
            if (command.Opcode == SacredScriptPortalOpcodes.If) { conditionalDepth++; continue; }
            if (command.Opcode == SacredScriptPortalOpcodes.EndBlock && conditionalDepth > 0)
            { conditionalDepth--; continue; }
            if (conditionalDepth != 0 || command.Opcode != SacredScriptDoorOpcodes.SetObjectState) continue;
            var args = SacredScriptDoorArguments.Read(command);
            if (args?.Name is not { } name) continue;
            _objectStates.TryGetValue(name, out var previous);
            _objectStates[name] = (args.Open ?? previous.Open, args.Locked ?? previous.Locked);
        }
    }

    public WorldDoorDefinition Resolve(SacredScriptCreateObject creation, int fileOffset)
    {
        _triggers.TryGetValue(creation.TriggerReference ?? string.Empty, out var trigger);
        _objectStates.TryGetValue(creation.Name ?? string.Empty, out var state);
        return new(trigger?.Id ?? _sourcePrefix + (uint)fileOffset,
            creation.Name, creation.TriggerReference,
            state.Open ?? trigger?.Open ?? false,
            state.Locked ?? (trigger is null || !trigger.Known || trigger.Locked),
            trigger?.Cells.ToArray() ?? []);
    }

    private sealed class Trigger(uint id, bool open, bool locked, bool known)
    {
        public uint Id { get; } = id;
        public bool Open { get; } = open;
        public bool Locked { get; } = locked;
        public bool Known { get; } = known;
        public List<SacredScriptPosition> Cells { get; } = [];
    }
}
