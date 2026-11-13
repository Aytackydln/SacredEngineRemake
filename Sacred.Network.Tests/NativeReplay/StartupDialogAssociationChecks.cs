using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

internal static class StartupDialogAssociationChecks
{
    public static int Run(string root)
    {
        using var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-dialog-associations.json")));
        var checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("Dialog associations: "+why);checks++;}
        foreach(var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var context=new WorldScriptExecutionContext(new(),[],ReadOnlyMemory<byte>.Empty,true);
            context.Registrations.Import(Bytes(row,"registrations_hex").Chunk(80).Select(bytes=>SacredScriptDialog.FromPayload(bytes)));
            var initial=Bytes(row,"initial_hex");context.DialogAssociations.Import(initial.Chunk(88).Select(bytes=>WorldScriptDialogAssociation.FromPayload(bytes)));
            new WorldScriptExecutor(context).ExecuteStartup(Bytes(row,"command_hex"));
            var after=Bytes(row,"after_hex");
            Check(context.StartupCompleted&&context.DialogAssociations.Records.Count*88==after.Length,"native append/update count");
            for(var index=0;index<context.DialogAssociations.Records.Count;index++)
            {
                var actual=context.DialogAssociations.Records[index];var expected=WorldScriptDialogAssociation.FromPayload(after.AsSpan(index*88,88));
                Check(actual.RegistrationSlot==expected.RegistrationSlot&&actual.TypeReference==expected.TypeReference&&actual.Qualifier==expected.Qualifier&&actual.DefaultWord72==expected.DefaultWord72,"native slot/default/type/qualifier");
                if(index*88<initial.Length)Check(actual.HasCompleteBytes&&actual.ToPayload().AsSpan().SequenceEqual(after.AsSpan(index*88,88)),"existing 88-byte row retained outside native updates");
                else
                {
                    Check(!actual.HasCompleteBytes,"new caller-scratch bytes are explicitly unknown");
                    try{_=actual.ToPayload();Check(false,"unknown scratch must not serialize");}
                    catch(InvalidDataException){checks++;}
                }
            }
        }
        return checks;
    }
    private static byte[] Bytes(JsonElement row,string name)=>Convert.FromHexString(row.GetProperty(name).GetString()!);
}
