using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Executes native flat branch order against changing state. A missing world handler
/// stops the run; enumerated commands and registered callbacks are never counted as executed effects.</summary>
public sealed class WorldScriptExecutor
{
    private readonly WorldScriptExecutionContext _context;
    private readonly Func<WorldScriptExecutionContext, SacredScriptCommand, bool>? _worldHandler;
    private readonly List<string> _calls = [];
    private int _remaining;
    public WorldScriptExecutor(WorldScriptExecutionContext context,
        Func<WorldScriptExecutionContext, SacredScriptCommand, bool>? worldHandler = null, int instructionBudget = 1_000_000)
    {
        if (instructionBudget <= 0) throw new ArgumentOutOfRangeException(nameof(instructionBudget));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _worldHandler = worldHandler;
        _remaining = instructionBudget;
    }

    public void ExecuteStartup(ReadOnlyMemory<byte> code, CancellationToken cancellationToken = default)
    {
        RequireValid();
        if (_context.StartupCompleted) throw new InvalidOperationException("Startup already executed for this context.");
        _context.IsExecutingStartup = true;
        try
        {
            Execute("StartCode.bin", code, 0, cancellationToken);
            if (_context.EquipmentPoolReload is { } pools) _context.EquipmentPools.Reload(pools);
            _context.StartupCompleted = true;
        }
        finally { _context.IsExecutingStartup = false; }
    }

    public void Invoke(int functionIndex, CancellationToken cancellationToken = default)
    {
        RequireValid();
        if (cancellationToken.IsCancellationRequested)
        {
            _context.IsInvalidated = true;
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (functionIndex == 0) return;
        var function = _context.RequireFunction(functionIndex);
        // Gold 0x46BA90 reserves index zero and tests only the deleted flag's low byte.
        if (unchecked((byte)function.Deleted) != 0) return;
        // Native dispatcher excludes these pool/dynamic-quest entry points from ordinary invocation.
        if (HasPrefix(function.Name, "InitRg") && (function.Name.Contains("Pools", StringComparison.Ordinal) ||
            function.Name.Contains("_DQ_Quest_", StringComparison.Ordinal)) || HasPrefix(function.Name, "DQTrgRegion") ||
            HasPrefix(function.Name, "DQStartRegion")) return;
        if (function.Quest >= 0)
        {
            var active = _context.QuestActive?.Invoke(function.Quest) ?? _context.Quests?.IsActive(function.Quest);
            if (active is null) throw new InvalidDataException($"Function '{function.Name}' requires active quest state for {function.Quest}.");
            if (!active.Value) return;
        }
        if (_calls.Count >= 128) throw new InvalidDataException("Script call depth exceeds the managed execution limit.");
        _calls.Add(function.Name);
        try { Execute("FunkCode.bin", _context.FunctionCode.Slice(function.Start, function.ByteLength), function.Start, cancellationToken); }
        finally { _calls.RemoveAt(_calls.Count - 1); }
    }

    /// <summary>Sector/region callback lookup permits absent names. A present function still
    /// executes normally and fails on unresolved world effects, predicates or quest state.</summary>
    public bool InvokeOptional(string name, CancellationToken cancellationToken = default)
    {
        RequireValid();
        ArgumentNullException.ThrowIfNull(name);
        if (cancellationToken.IsCancellationRequested)
        {
            _context.IsInvalidated = true;
            cancellationToken.ThrowIfCancellationRequested();
        }
        for (var index = 0; index < _context.Functions.Count; index++)
            if (WorldScriptNameComparer.Instance.Equals(_context.Functions[index].Name, name))
            {
                Invoke(index, cancellationToken);
                return true;
            }
        return false;
    }

    private void Execute(string script, ReadOnlyMemory<byte> code, int fileOffset, CancellationToken cancellationToken)
    {
        var previousCallerIndex = _context.VariableCallerIndex;
        var previousCallerStopsImmediately = _context.VariableCallerStopsImmediately;
        _context.VariableCallerIndex = null;
        SacredScriptCommand current = new(fileOffset, code.Length >= 4 ? code[..4] : code);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Restart:
            foreach (var relative in SacredScriptControlFlowReader.Read(code, Predicate))
            {
                current = relative with { FileOffset = fileOffset + relative.FileOffset };
                CheckBudget();
                if (!WorldScriptVariableCallerEffects.Preserves(_context, current))
                    _context.VariableCallerIndex = null;
                Trace(current, false);
                _context.BeforeCommand?.Invoke(current);
                switch (current.Opcode)
                {
                    case SacredScriptControlOpcodes.Nop: break;
                    case SacredScriptExecutionOpcodes.RestartFunction when script == "FunkCode.bin":
                        _context.Record(current.Opcode);
                        goto Restart;
                    case SacredScriptCommandHeaderLayout.CreateObjectOpcode when _context.ObjectWorld is not null:
                        _context.ObjectWorld.Create(_context, current);
                        break;
                    case SacredScriptCreateCreature.Opcode when _context.CreatureWorld is not null:
                        _context.CreatureWorld.Create(_context, current);
                        WorldScriptVariableCallerEffects.CompleteCreature(_context, current);
                        break;
                    case SacredScriptDoorOpcodes.SetObjectState when _context.CreatureWorld is IWorldScriptObjectStateWorld objectState:
                        objectState.SetObjectState(_context,current);
                        break;
                    case SacredScriptCommandHeaderLayout.SetCreatureStateOpcode when _context.CreatureWorld is IWorldScriptCreatureStateWorld state:
                        state.SetState(_context,current);
                        break;
                    case SacredScriptExecutionOpcodes.SetCreatureBaseAttribute when _context.CreatureWorld is IWorldScriptCreatureAttributeWorld attributes:
                        attributes.SetBaseAttribute(_context, current);
                        break;
                    case SacredScriptExecutionOpcodes.SetCreatureHitPoints when _context.CreatureWorld is IWorldScriptCreatureHealthWorld health:
                        health.SetHitPoints(_context, current);
                        break;
                    case SacredScriptCommandHeaderLayout.EquipItemOpcode when _context.CreatureWorld is IWorldScriptEquipmentWorld equipment:
                        equipment.Equip(_context, current);
                        break;
                    case SacredScriptExecutionOpcodes.QuestBook:
                        WorldScriptQuestBook.Apply(_context,current);
                        break;
                    case SacredScriptExecutionOpcodes.QuestCompassPosition:
                        WorldScriptQuestCompass.Apply(_context, current);
                        break;
                    case SacredScriptExecutionOpcodes.QuestCompassObject:
                        WorldScriptQuestObjectCompass.Apply(_context, current);
                        break;
                    case SacredScriptExecutionOpcodes.PlayerInformation:
                        WorldScriptPlayerInformation.Apply(_context, current);
                        break;
                    case SacredScriptExecutionOpcodes.Popup:
                        WorldScriptPopup.Apply(_context, current);
                        break;
                    case SacredScriptExecutionOpcodes.TriggerQuest:
                        WorldScriptQuestTrigger.Apply(_context,current,
                            index=>WorldScriptEmptyFunctionResult.Invoke(_context,index,callback=>Invoke(callback,cancellationToken)),
                            index=>Invoke(index,cancellationToken));
                        break;
                    case SacredScriptExecutionOpcodes.ActivateQuest:
                        if (_context.Quests is null) throw new InvalidDataException("Quest activation requires the original quest table.");
                        _context.Quests.Execute(current, _context.IsExecutingStartup, index => Invoke(index, cancellationToken));
                        break;
                    case SacredScriptExecutionOpcodes.CallFunction:
                        Call(current);
                        break;
                    default:
                        if (!WorldScriptSectorHandlers.Execute(_context, current) &&
                            !WorldScriptDefinitionHandlers.Execute(_context, current) && _worldHandler?.Invoke(_context, current) != true)
                            throw new InvalidDataException($"Missing world handler for opcode {current.Opcode}.");
                        break;
                }
                _context.Record(current.Opcode);
            }
        }
        catch (OperationCanceledException) { _context.IsInvalidated = true; throw; }
        catch (WorldScriptExecutionFailure) { throw; }
        catch (Exception error)
        {
            _context.IsInvalidated = true;
            var opcode = current.Bytes.Length >= 2 ? current : new(current.FileOffset, new byte[4]);
            _context.Failure = new(script, opcode, _calls, error);
            throw _context.Failure;
        }
        finally
        {
            _context.VariableCallerIndex = previousCallerIndex;
            _context.VariableCallerStopsImmediately = previousCallerStopsImmediately;
        }

        bool Predicate(SacredScriptCommand relative)
        {
            current = relative with { FileOffset = fileOffset + relative.FileOffset };
            CheckBudget();
            _context.VariableCallerIndex = null;
            Trace(current, true);
            var value = SacredScriptConditionReader.Evaluate(current, _context.Multiplayer, _context.Variables.Find) ??
                WorldScriptHeroPredicates.Evaluate(_context,current);
            if (value is null) throw new InvalidDataException("Unresolved predicate; a branch cannot be chosen.");
            _context.Record(current.Opcode);
            return value.Value;
        }
        void CheckBudget()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--_remaining < 0) throw new InvalidDataException("Script instruction budget exhausted.");
        }
        void Trace(SacredScriptCommand command, bool predicate)
        {
            if (_context.TraceCommand is { } observer)
                observer(new(script, command with { Bytes = command.Bytes.ToArray() },
                    Array.AsReadOnly(_calls.ToArray()), predicate));
        }
        void Call(SacredScriptCommand command)
        {
            var operands = SacredScriptOperandReader.Read(command);
            if (operands.Count != 1 || operands[0].Kind != SacredScriptArgumentKind.NullTerminatedString)
                throw new InvalidDataException("Function call requires native object/resource association semantics for these operands.");
            Invoke(_context.FindFunction(WorldScriptDefinitionHandlers.Text(operands[0])), cancellationToken);
        }
    }

    private void RequireValid()
    {
        if (_context.IsInvalidated) throw new InvalidOperationException("Discard the failed/cancelled script context before retrying.");
    }
    private static bool HasPrefix(string value, string prefix) => value.Length >= prefix.Length &&
        WorldScriptNameComparer.Instance.Equals(value[..prefix.Length], prefix);
}
