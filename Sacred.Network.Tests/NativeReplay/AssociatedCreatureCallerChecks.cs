using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using System.Text.Json;
using System.Buffers.Binary;

internal static class AssociatedCreatureCallerChecks
{
    public static int Run(string root)
    {
        using var native=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-function-scope.json")));
        var checks=0;
        using(var dispatch=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-associated-callback-vm.json"))))
        using(var dialog=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-associated-dialog.json"))))
        foreach(var sample in dispatch.RootElement.GetProperty("cases").EnumerateArray().GroupBy(row=>row.GetProperty("opcode").GetUInt16()).Select(group=>group.First()))
        {
            var opcode=sample.GetProperty("opcode").GetUInt16();var code=new byte[4];
            BinaryPrimitives.WriteUInt16LittleEndian(code,opcode);BinaryPrimitives.WriteUInt16LittleEndian(code.AsSpan(2),4);
            if(opcode==SacredScriptCommandHeaderLayout.SetCreatureStateOpcode)code=Convert.FromHexString(dialog.RootElement.GetProperty("cases")[0].GetProperty("command_hex").GetString()!);
            WorldScriptExecutionContext context=null!;var stop=new InvalidOperationException("Controlled handler-entry boundary");
            context=new(new(),[Empty(),new("dispatch",0,code.Length,-1,0,0)],code,true)
            {
                BeforeCommand=_=>{Check(context.AssociatedCreatureReference==17,"native handler kind receives retained typed creature identity");throw stop;}
            };
            try{new WorldScriptExecutor(context).InvokeAssociatedCreature(1,17);throw new Exception("Handler boundary did not execute");}
            catch(WorldScriptExecutionFailure failure)
            {
                Check(ReferenceEquals(failure.InnerException,stop),"compared native handler kind reaches observer rather than unsupported-caller guard");
                Check(context.IsInvalidated&&context.AssociatedCreatureReference==0,"handler-entry interruption restores caller scope");
            }
        }
        foreach(var reference in new uint[]{1,16,17,GoldReferenceCodec.ReferenceLimit-1})
        {
            var seen=new List<uint>();
            WorldScriptExecutionContext context=null!;
            WorldScriptExecutor executor=null!;
            var nested=false;
            context=new(new(),[Empty(),new("outer",0,4,-1,0,0),new("inner",0,4,-1,0,0)],new byte[]{62,0,4,0},true)
            {
                BeforeCommand=_=>
                {
                    seen.Add(context.AssociatedCreatureReference);
                    if(nested)return;
                    nested=true;executor.InvokeAssociatedCreature(2,2);
                    seen.Add(context.AssociatedCreatureReference);
                }
            };
            executor=new(context);executor.InvokeAssociatedCreature(1,reference);
            Check(seen.SequenceEqual(new[]{reference,2u,reference})&&context.AssociatedCreatureReference==0,"nested caller identities and restoration");
            if(reference==17)Check(seen.SequenceEqual(native.RootElement.GetProperty("nested").GetProperty("actor_events").EnumerateArray().Select(value=>value.GetUInt32())),"actual nested native dispatcher actor arguments and caller restoration");
            Check(context.HasActiveHero is null&&context.LocalPlayerSlot is null,"actor identity does not invent active-hero or local-slot inputs");
        }
        foreach(var reference in new uint[]{0,GoldReferenceCodec.ReferenceLimit,uint.MaxValue})
        {
            var context=new WorldScriptExecutionContext(new(),[Empty()],ReadOnlyMemory<byte>.Empty,true);
            try {new WorldScriptExecutor(context).InvokeAssociatedCreature(0,reference);throw new Exception("Invalid caller accepted");}
            catch(ArgumentOutOfRangeException){Check(context.AssociatedCreatureReference==0&&!context.IsInvalidated,"invalid identity has no scope or effects");}
        }
        foreach(var code in new[]{new byte[]{67,0,4,0},new byte[]{58,0,5,0,0xa0},
            new byte[]{3,0,7,0,10,0,0},new byte[]{3,0,9,0,1,0,10,0,0}})
        {
            var attempted=0;
            var context=new WorldScriptExecutionContext(new(),[Empty(),new("guarded",0,code.Length,-1,0,0)],code,true){BeforeCommand=_=>attempted++};
            try {new WorldScriptExecutor(context).InvokeAssociatedCreature(1,17);throw new Exception("Unverified caller effect accepted");}
            catch(WorldScriptExecutionFailure)
            {
                Check(context.IsInvalidated&&context.AssociatedCreatureReference==0&&context.ExecutedCommands==0&&attempted==0,"uncompared commands/predicates reject before observer, effects or branch choice");
            }
        }
        var cancelled=new WorldScriptExecutionContext(new(),[Empty()],ReadOnlyMemory<byte>.Empty,true);
        try {new WorldScriptExecutor(cancelled).InvokeAssociatedCreature(0,17,new CancellationToken(true));throw new Exception("Cancellation ignored");}
        catch(OperationCanceledException){Check(cancelled.IsInvalidated&&cancelled.AssociatedCreatureReference==0,"cancelled invocation restores caller scope");}
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("Associated creature: "+why);checks++;}
    }
    private static SacredScriptFunction Empty()=>new("",0,0,-1,0,0);
}
