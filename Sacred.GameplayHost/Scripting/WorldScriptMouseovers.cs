using System.Buffers.Binary;
using System.Text;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Stored record plus original coordinates used by the direct tile query.</summary>
public sealed record WorldScriptMouseoverCreation(GoldSectorScriptPatch Patch, SacredScriptPosition Position);

/// <summary>Gold 0x4A6950 opcode 94. Every record appends to pending; active registration keeps the first XYZ key.</summary>
public sealed class WorldScriptMouseovers
{
    public const ushort Opcode = 94;
    private readonly List<GoldSectorScriptPatch> _pending = [];
    private readonly Func<string, uint> _resource;
    private readonly Action<WorldScriptExecutionContext, WorldScriptMouseoverCreation> _mark;
    public WorldScriptMouseovers(Func<string, uint> resource,
        Action<WorldScriptExecutionContext, WorldScriptMouseoverCreation> mark)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(mark);
        (_resource, _mark) = (resource, mark);
        Pending = _pending.AsReadOnly();
    }
    public IReadOnlyList<GoldSectorScriptPatch> Pending { get; }
    public GoldMouseoverRegistry Active { get; } = new();

    public void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (!context.IsExecutingStartup) throw new InvalidDataException("Live mouseover creation requires publication and entered-sector state.");
        var creation = Read(command, name => _resource(WorldScriptVariableNames.Expand(name,context.Variables.Find)), name => context.Definitions.ResolvePosition(name, context.Random, context.PositionAllowed));
        if (creation is null) return;
        _pending.Add(creation.Patch);
        Active.Register(creation.Patch);
        _mark(context, creation);
    }

    internal void RegisterPool(WorldScriptExecutionContext context, GoldSectorScriptPatch patch, int x, int y, bool mark)
    {
        _pending.Add(patch);
        Active.Register(patch);
        if (mark) _mark(context,new(patch,new(x,y,0)));
    }

    /// <summary>Compared record construction; external resource and symbolic-position resolution are explicit.</summary>
    public static WorldScriptMouseoverCreation? Read(SacredScriptCommand command, Func<string,uint> resource,
        Func<string,SacredScriptPosition> position)
    {
        if (command.Opcode != Opcode) throw new ArgumentException("Expected opcode 94.", nameof(command));
        var x = -1; var y = -1; var surface = -1;
        var firstX = 0; var firstY = 0; var lastX = 0; var lastY = 0;
        uint id = uint.MaxValue; var callback = ""; ushort range = 16000; var mode = 0;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            switch (operand.Kind)
            {
                case SacredScriptArgumentKind.TilePosition:
                    var tile = operand.Position ?? position(operand.Text!);
                    (x, y, surface) = (tile.X, tile.Y, tile.Z);
                    break;
                case SacredScriptArgumentKind.TriggerFirstCorner:
                    var first = operand.Position ?? position(operand.Text!);
                    (firstX, firstY) = (first.X, first.Y);
                    break;
                case SacredScriptArgumentKind.TriggerLastCorner:
                    var last = operand.Position ?? position(operand.Text!);
                    (lastX, lastY) = (last.X, last.Y);
                    break;
                case SacredScriptArgumentKind.ScriptResourceId: id = unchecked((uint)operand.Integer!.Value); break;
                case SacredScriptArgumentKind.ScriptResourceName: id = resource(operand.Text!); break;
                case SacredScriptArgumentKind.ObjectTakeScript:
                    callback = operand.Text!;
                    if (callback.Any(character => character > 255)) throw new InvalidDataException("Mouseover callback must preserve Latin-1 bytes.");
                    break;
                case SacredScriptArgumentKind.MouseoverMode1: mode = 1; break;
                case SacredScriptArgumentKind.MouseoverMode2: mode = 2; break;
                case SacredScriptArgumentKind.MouseoverShortRange: range = 1; break;
                // Successfully decoded operands outside the native switch are ignored.
            }
        }
        if (unchecked((int)id) < 0 && callback.Length == 0 || x < 0 || y < 0 || surface < 0) return null;
        var bytes = new byte[GoldSectorScriptPatch.Size];
        void Word(int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), unchecked((ushort)value));
        Word(0,x); Word(2,y); Word(4,surface); Word(6,range);
        Word(8,firstX); Word(10,firstY); Word(12,lastX); Word(14,lastY);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16),id);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20),mode);
        Encoding.Latin1.GetBytes(callback.AsSpan(0,Math.Min(callback.Length,63)),bytes.AsSpan(24));
        return new(GoldSectorScriptPatch.FromPayload(bytes),new(x,y,surface));
    }
}
