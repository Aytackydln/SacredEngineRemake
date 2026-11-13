using Sacred.Assets.GameBin;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Original post-StartCode campaign callback selection and supported
/// null-actor execution. Non-null actor binding stays pending before any effects;
/// current startup factories implement only the independently compared unbound caller.</summary>
public sealed class WorldScriptPostStartup(WorldScriptExecutionContext context)
{
    public const string CampaignCallback = "MultiCampaign";
    private WorldScriptPostStartupInputs? _inputs;
    public WorldScriptPostStartupInvocation? Invocation { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool RequiresActorBinding => Invocation is { FunctionIndex: not 0, ActorReference: not 0 } && !IsCompleted;

    /// <summary>Gold 0x470BBE..0x470CBF. First complete C-locale name match wins;
    /// missing names and a false connection/campaign gate select no invocation.</summary>
    public static WorldScriptPostStartupInvocation? Resolve(bool multiplayer,
        WorldScriptPostStartupInputs inputs, IReadOnlyList<SacredScriptFunction> functions)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(functions);
        if (!multiplayer || !inputs.ConnectionPresent || !inputs.Connected || !GoldSessionGameFlagsCodec.IsCampaignGame(inputs.GameFlags)) return null;
        for (var i = 0; i < functions.Count; i++)
            if (WorldScriptNameComparer.Instance.Equals(functions[i].Name, CampaignCallback))
                return new(i, inputs.LocalActorReference);
        return null;
    }

    public bool TryExecute(WorldScriptPostStartupInputs inputs, CancellationToken cancellationToken = default)
    {
        if (context.IsInvalidated) throw new InvalidOperationException("Discard the invalidated script context before post-startup work.");
        if (!context.StartupCompleted) throw new InvalidOperationException("Post-startup work requires completed StartCode and pool reload.");
        if (IsCompleted) throw new InvalidOperationException("Post-startup callback stage already completed.");
        if (cancellationToken.IsCancellationRequested)
        {
            context.IsInvalidated = true;
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (_inputs is not null && _inputs != inputs)
            throw new InvalidOperationException("Post-startup inputs changed after invocation capture; discard stale work.");
        if (_inputs is null)
        {
            _inputs = inputs;
            Invocation = Resolve(context.Multiplayer, inputs, context.Functions);
        }
        if (RequiresActorBinding)
        {
            Console.WriteLine($"Post-startup campaign callback pending: function {Invocation!.FunctionIndex}, actor {Invocation.ActorReference} needs native caller binding.");
            return false;
        }
        var previousStartup = context.IsExecutingStartup;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Invocation is { } invocation)
            {
                // Native multiplayer loader retains interpreter +0x14 through this call.
                context.IsExecutingStartup = true;
                Console.WriteLine($"Post-startup campaign callback: function {invocation.FunctionIndex}, actor {invocation.ActorReference}.");
                new WorldScriptExecutor(context).Invoke(invocation.FunctionIndex, cancellationToken);
            }
            IsCompleted = true;
            Console.WriteLine("Post-startup campaign callback stage complete; sector work and host readiness remain separate.");
            return true;
        }
        catch { context.IsInvalidated = true; throw; }
        finally { context.IsExecutingStartup = previousStartup; }
    }
}
