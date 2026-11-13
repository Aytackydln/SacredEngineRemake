using Sacred.Core.GameBin.Scripts;
using Sacred.Assets.GameBin;

namespace Sacred.GameplayHost.Scripting;

/// <summary>The first unresolved executed operation. Includes the call path and original file offset.</summary>
public sealed class WorldScriptExecutionFailure : Exception
{
    public WorldScriptExecutionFailure(string script, SacredScriptCommand command, IReadOnlyList<string> calls, Exception cause)
        : base($"{script} at 0x{command.FileOffset:X}, opcode {command.Opcode}: {cause.Message}" +
               $" [instruction: {Describe(command)}]" +
               (calls.Count == 0 ? string.Empty : $" (calls: {string.Join(" -> ", calls)})"), cause)
    {
        Script = script;
        Offset = command.FileOffset;
        Opcode = command.Opcode;
        CallPath = Array.AsReadOnly(calls.ToArray());
    }
    public string Script { get; }
    public int Offset { get; }
    public ushort Opcode { get; }
    public IReadOnlyList<string> CallPath { get; }

    private static string Describe(SacredScriptCommand command)
    {
        try { return SacredScriptDisassembler.Format(command); }
        catch (InvalidDataException) { return $"raw={Convert.ToHexString(command.Bytes.Span)}"; }
    }
}
