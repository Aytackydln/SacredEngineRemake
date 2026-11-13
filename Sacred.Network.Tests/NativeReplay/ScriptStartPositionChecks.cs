using System.Text.Json;
using Sacred.GameplayHost.Scripting;

internal static class ScriptStartPositionChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-script-start-position.json")));
        var checks=0;var positiveControls=0;
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var multiplayer=row.GetProperty("multiplayer").GetBoolean();
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,multiplayer);
            var command=Convert.FromHexString(row.GetProperty("command_hex").GetString()!);
            if(multiplayer)
            {
                new WorldScriptExecutor(context).ExecuteStartup(command);
                Check(context.StartupCompleted && context.ExecutedCommands==1 && !context.IsInvalidated,"complete multiplayer command");
                Check(row.GetProperty("calls").GetArrayLength()==0 && row.GetProperty("hero_unchanged").GetBoolean(),"native multiplayer skips both teleport and hero checkpoint copy");
                Check(context.Variables.Records.Count==0 && context.Definitions.Records.Count==0 && context.ObjectCallbacks.Records.Count==0,"no invented script mutations");
            }
            else
            {
                positiveControls+=row.GetProperty("calls").GetArrayLength();
                try { new WorldScriptExecutor(context).ExecuteStartup(command);throw new Exception("Offline StartPosition accepted"); }
                catch(WorldScriptExecutionFailure) { Check(context.IsInvalidated && context.ExecutedCommands==0 && !context.StartupCompleted,"single-player lifecycle remains guarded"); }
            }
        }
        Check(positiveControls>0,"native offline positive controls exercise the teleport boundary");
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("StartPosition: "+why);checks++;}
    }
}
