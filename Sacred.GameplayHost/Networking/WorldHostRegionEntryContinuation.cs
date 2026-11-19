using Sacred.Core.GameBin.Scripts;
using Sacred.Assets.GameBin;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Continue the same actual first server instruction into EnterRegion.
/// Region/sector overrides outside the reached same-region profile stay guarded.</summary>
internal static class WorldHostRegionEntryContinuation
{
    public static async Task<bool?> RunOwnedAsync(WorldHostInitializationSession session,
        WorldHostSectorInitialization owner, SemaphoreSlim gate,
        CancellationToken lifetime, CancellationToken token)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(lifetime,token);
        await gate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if(!session.IsCurrent)throw new InvalidOperationException("Region entry needs its live host receipt.");
            var hero=session.ActiveHero ?? throw new InvalidOperationException("Region entry needs the actual entered hero.");
            hero.RequireCurrent();
            var startup=session.Startup ?? throw new InvalidOperationException("Region entry needs completed startup.");
            var context=startup.Execution;
            if(context.IsInvalidated || !context.PostStartup.IsCompleted)throw new InvalidOperationException("Region entry needs valid completed campaign work.");
            var region=session.RegionInitialization ?? throw new InvalidOperationException("Region entry requires the initialized region.");
            if (!ReferenceEquals(region.InitialSector, owner)) throw new InvalidOperationException("Region entry belongs to a different sector instruction.");
            var init=owner;
            var current=session.Replicas.SectorWork.Work.Values.OrderBy(work=>work.Id)
                .FirstOrDefault(work=>work.State is GoldSectorWorkState.Queued or GoldSectorWorkState.Executing);
            if(!init.IsCompleted || !ReferenceEquals(current,init.Instruction) || current?.State!=GoldSectorWorkState.Executing || !current.ExecuteScripts)
                throw new NotSupportedException("Region entry needs the same executing server instruction and completed Init.");
            var selected=region.ScriptRegion(current.Entry.Sector);
            if(region.ScriptRegion(hero.Actor.Base.Sector)!=selected)
                throw new NotSupportedException("Region entry's differing hero-sector override requires its remaining native caller profile.");
            region.Entry ??= new(region.Lifecycle);
            init.EnterRegionBlocker=null;
            try
            {
                return region.Entry.TryEnter(selected,true,region.FindSectorFunction,region.FindEntryFunction,index=>
                {
                    linked.Token.ThrowIfCancellationRequested();
                    if(index==0 || index>=context.Functions.Count)return true;
                    var function=context.Functions[index];
                    foreach(var command in SacredCompiledScriptReader.Read(context.FunctionCode.Slice(function.Start,function.ByteLength)))
                    {
                        if(command.Opcode is SacredScriptControlOpcodes.Stop or SacredScriptControlOpcodes.Nop or SacredScriptControlOpcodes.BranchEnd)continue;
                        if(command.Opcode is SacredScriptControlOpcodes.If or SacredScriptControlOpcodes.ElseIf &&
                            SacredScriptConditionReader.Evaluate(command,context.Multiplayer,context.Variables.Find) is not null)continue;
                        if(WorldScriptEnteredVariableBits.Supports(command) &&
                            context.Variables.Find(SacredScriptOperandReader.Read(command)[0].Text!) is not null)continue;
                        if(command.Opcode==SacredScriptExecutionOpcodes.CallFunction && WorldScriptRegionCallbackProfile.IsExcludedCall(context,command))continue;
                        init.EnterRegionBlocker=$"Region entry callback '{function.Name}' (function {index}) requires opcode {command.Opcode} at 0x{function.Start+command.FileOffset:X}; no body executed.";
                        return false;
                    }
                    context.IsEnteringRegion=true;
                    try{new WorldScriptExecutor(context).Invoke(index,linked.Token);return true;}
                    catch{context.IsInvalidated=true;throw;}
                    finally{context.IsEnteringRegion=false;}
                });
            }
            finally
            {
                hero.RequireCurrent();
                if(!session.IsCurrent)throw new InvalidOperationException("Host receipt ended during region entry.");
            }
        }
        finally{gate.Release();}
    }
}
