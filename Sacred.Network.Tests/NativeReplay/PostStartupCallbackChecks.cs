using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class PostStartupCallbackChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-post-startup-callback.json")));
        var checks = 0;
        foreach (var sample in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var names = sample.GetProperty("names").EnumerateArray().Select(value => value.GetString()!).ToArray();
            Compare(sample, names, execute: true);
        }
        var installedNames = fixture.RootElement.GetProperty("installed_names").EnumerateArray().Select(value => value.GetString()!).ToArray();
        foreach (var sample in fixture.RootElement.GetProperty("installed").EnumerateArray())
            Compare(sample, installedNames, execute: false);

        var code = new byte[] { 62, 0, 4, 0 };
        var functions = new[] { Function("reserved"), new SacredScriptFunction("MultiCampaign", 0, 4, -1, 0, 0) };
        WorldScriptExecutionContext? active = null;
        var observedStartup = false;
        active = new(new(), functions, code, true) { BeforeCommand = _ => observedStartup = active!.IsExecutingStartup };
        var inputs = new WorldScriptPostStartupInputs(true, true, 0x10, 0);
        Reject(() => active.PostStartup.TryExecute(inputs), "post-startup cannot precede bytecode completion");
        new WorldScriptExecutor(active).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
        Check(active.PostStartup.TryExecute(inputs) && observedStartup && !active.IsExecutingStartup && active.ExecutedCommands == 1,
            "supported null actor executes inside native startup mode and restores caller scope");
        Reject(() => active.PostStartup.TryExecute(inputs), "completed stage cannot run twice");
        var pending = Context(functions, code);
        Check(!pending.PostStartup.TryExecute(inputs with { LocalActorReference = 2 }) && pending.ExecutedCommands == 0 && !pending.IsInvalidated,
            "bound actor waits before startup factories can use the wrong caller profile");
        Reject(() => pending.PostStartup.TryExecute(inputs), "captured actor binding cannot silently change on retry");
        var cancelled = Context(functions, code);
        var cancellationObserved = false;
        try { cancelled.PostStartup.TryExecute(inputs with { LocalActorReference = 2 }, new CancellationToken(true)); }
        catch (OperationCanceledException) { cancellationObserved = true; }
        Check(cancellationObserved && cancelled.IsInvalidated && cancelled.ExecutedCommands == 0, "cancellation invalidates before pending or executed effects");
        var failed = Context(functions, code, _ => throw new NotSupportedException("target effects unavailable"));
        try { failed.PostStartup.TryExecute(inputs); }
        catch (WorldScriptExecutionFailure)
        {
            Check(failed.IsInvalidated && !failed.PostStartup.IsCompleted && !failed.IsExecutingStartup,
                "failed callback remains incomplete and restores startup scope");
            Reject(() => failed.PostStartup.TryExecute(inputs), "failed callback cannot be retried in partial state");
            return checks;
        }
        throw new Exception("Unsupported callback effects were accepted.");

        void Compare(JsonElement sample, string[] names, bool execute)
        {
            var inputs = new WorldScriptPostStartupInputs(sample.GetProperty("present").GetBoolean(),
                sample.GetProperty("active").GetInt32() != 0, sample.GetProperty("flags").GetUInt32(), sample.GetProperty("actor_reference").GetUInt32());
            var multiplayer = sample.GetProperty("multiplayer").GetBoolean();
            var functions = names.Select(Function).ToArray();
            var request = WorldScriptPostStartup.Resolve(multiplayer, inputs, functions);
            var calls = sample.GetProperty("calls");
            Check((request is null) == (calls.GetArrayLength() == 0), "original multiplayer/connection/campaign gates and absent function");
            if (request is not null)
            {
                Check(request.FunctionIndex == calls[0].GetProperty("index").GetInt32(), "native first full C-locale match, including reserved index zero");
                Check(request.ActorReference == calls[0].GetProperty("actor_reference").GetUInt32(), "actual native local actor argument");
            }
            Check(sample.GetProperty("actor_queries").GetInt32() == (multiplayer && inputs.ConnectionPresent && inputs.Connected &&
                GoldSessionGameFlagsCodec.IsCampaignGame(inputs.GameFlags) ? 1 : 0), "native gate precedes local actor query and name lookup");
            if (!execute) return;
            var context = new WorldScriptExecutionContext(new(), functions, ReadOnlyMemory<byte>.Empty, multiplayer);
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            var complete = context.PostStartup.TryExecute(inputs);
            var bound = request is { FunctionIndex: not 0, ActorReference: not 0 };
            Check(complete == !bound && context.PostStartup.RequiresActorBinding == bound, "only explicitly supported actor profile receives execution credit");
            Check(context.PostStartup.Invocation == request && context.ExecutedCommands == 0 && !context.IsInvalidated,
                "request is retained independently from completion, pending work has no partial effects");
        }
        WorldScriptExecutionContext Context(SacredScriptFunction[] functions, byte[] code, Action<SacredScriptCommand>? before = null)
        {
            var context = new WorldScriptExecutionContext(new(), functions, code, true) { BeforeCommand = before };
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            return context;
        }
        void Check(bool pass, string why) { if (!pass) throw new Exception("Native post-startup: " + why); checks++; }
        void Reject(Action action, string why)
        {
            try { action(); } catch (InvalidOperationException) { checks++; return; }
            throw new Exception("Accepted invalid post-startup: " + why);
        }
    }
    private static SacredScriptFunction Function(string name) => new(name, 0, 0, -1, 0, 0);
}
