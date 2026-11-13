using System.Buffers.Binary;
using System.Text;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World.Stairs;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x4B0C00 startup hideouts: bounded pool scan, random selection,
/// stable definition removal and mouseover registration. Count is not a placement radius.</summary>
public sealed class WorldScriptHideouts(Func<string,uint> resource, Func<int,int,uint> sectorFlags)
{
    public void Apply(WorldScriptExecutionContext context, SacredScriptCommand command)
    {
        if (!context.IsExecutingStartup) throw new InvalidDataException("Live hideout generation requires native publication and sector state.");
        var names = new List<string>(); int count = -1;
        foreach (var operand in SacredScriptOperandReader.Read(command))
        {
            if (operand.Kind == SacredScriptArgumentKind.NullTerminatedString)
            {
                if (names.Count == 5) continue;
                var text = WorldScriptVariableNames.Expand(operand.Text!,context.Variables.Find);
                if (text.Length >= 64 || text.Contains('$')) throw new InvalidDataException("Hideout operand requires native bounded string/resource resolution.");
                names.Add(text);
            }
            else if (operand.Kind == SacredScriptArgumentKind.Integer)
                count = operand.Integer ?? context.Variables.Find(operand.Text!) ?? throw new InvalidDataException("Hideout count expression is unresolved.");
            else if (operand.Kind is SacredScriptArgumentKind.ScriptResourceId or SacredScriptArgumentKind.ScriptResourceName)
                throw new InvalidDataException("Hideout tagged resource operands require their native string representation.");
        }
        if (count == -1)
        {
            if (names.Count != 4 || context.Variables.Find(names[3]) is not { } value) return;
            count = value; names.RemoveAt(3);
        }
        if (count <= 0 || names.Count != 3) return;
        var mouseovers = context.Mouseovers ?? throw new InvalidDataException("Hideout generation requires the shared mouseover registry.");
        var id = resource(names[0]);
        for (var iteration=0;iteration<count;iteration++)
        {
            var indices = context.Definitions.Records.Select((record,index)=>(record,index))
                .Where(pair=>pair.record.Kind==WorldDefinitionKind.PoolPosition && WorldScriptNameComparer.Instance.Equals(pair.record.Name,names[1]))
                .Take(100).Select(pair=>pair.index).ToArray();
            if (indices.Length==0) return;
            var selected=0;
            if (indices.Length>1)
            {
                var random=context.Random?.Invoke() ?? throw new InvalidDataException("Hideout selection requires the native shared RNG.");
                if (random is <0 or >32767) throw new InvalidDataException("Hideout RNG must return the native 15-bit value.");
                selected=random%indices.Length;
            }
            var definition=context.Definitions.TakeAt(indices[selected]);
            var x=definition.Parameter(0);var y=definition.Parameter(1);
            var bytes=new byte[GoldSectorScriptPatch.Size];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes,unchecked((ushort)x));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2),unchecked((ushort)y));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6),1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16),id);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20),2);
            Encoding.Latin1.GetBytes(names[2].AsSpan(0,Math.Min(names[2].Length,63)),bytes.AsSpan(24));
            mouseovers.RegisterPool(context,GoldSectorScriptPatch.FromPayload(bytes),x,y,(sectorFlags(x,y)&GoldSectorEntryRuntime.InitializedFlag)!=0);
        }
    }
}
