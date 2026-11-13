using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

namespace Sacred.Network.Tests;

[TestClass]
public sealed class ScriptCallerHistoryTests
{
    [TestMethod]
    public void UncomparedCommandAndPredicateInvalidateSuffix()
    {
        foreach (var separator in new[] { Command(62), Join(Command(58, [0xA0]), Command(62), Command(59), Command(62)) })
        {
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
            new WorldScriptExecutor(context).ExecuteStartup(Join(Variable("abcdefg12"), separator, Variable("x")));
            Assert.IsNull(context.VariableGlobals.Mutations.Last().Index);
        }
    }

    [TestMethod]
    public void NestedFunctionHasIndependentConservativeScratchAndTrace()
    {
        var child = Join(Variable("y"), Variable("abcdefg27"), Variable("z"));
        var observations = new List<WorldScriptCommandTrace>();
        var context = new WorldScriptExecutionContext(new(),
            [new("reserved", 0, 0, -1, 0, 0), new("child", 0, child.Length, -1, 1, 0)], child, true)
            { TraceCommand = observations.Add };
        var startup = Join(Variable("abcdefg12"), Command(22, Name("child")), Variable("x"));
        new WorldScriptExecutor(context).ExecuteStartup(startup);
        Assert.IsNull(context.VariableGlobals.Mutations.Single(value => value.Name == "y").Index);
        Assert.AreEqual(27, context.VariableGlobals.Mutations.Single(value => value.Name == "z").Index);
        Assert.IsNull(context.VariableGlobals.Mutations.Single(value => value.Name == "x").Index);
        Assert.IsTrue(observations.Where(value => value.Script == "FunkCode.bin").All(value => value.CallPath.SequenceEqual(["child"])));
        Assert.AreEqual(6, observations.Count);
        var retained = observations[0].Command.Bytes.ToArray();
        startup.AsSpan().Fill(0xCC);
        CollectionAssert.AreEqual(retained, observations[0].Command.Bytes.ToArray());
    }

    [TestMethod]
    public void TraceIncludesPredicatesAndOnlyVisitedBranchCommands()
    {
        var observations = new List<WorldScriptCommandTrace>();
        var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true)
            { TraceCommand = observations.Add };
        new WorldScriptExecutor(context).ExecuteStartup(Join(Command(58, [0xA0]), Variable("x"), Command(59), Command(62)));
        Assert.AreEqual(context.ExecutedCommands, observations.Count);
        Assert.IsTrue(observations[0].IsPredicate);
        Assert.IsTrue(observations.Skip(1).All(value => !value.IsPredicate));
        Assert.IsTrue(observations.All(value => value.CallPath.Count == 0));
    }

    [TestMethod]
    public void ImportedRegistrationCapacityCannotJustifyPreservingScratch()
    {
        var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true);
        context.Registrations.Import(Enumerable.Range(0, 3).Select(_ => SacredScriptDialog.FromPayload(new byte[80])));
        new WorldScriptExecutor(context).ExecuteStartup(Join(Variable("abcdefg12"), Command(40, new byte[80]), Variable("x")));
        Assert.IsNull(context.Registrations.EstablishedCapacity);
        Assert.IsNull(context.VariableGlobals.Mutations.Last().Index);
    }

    [TestMethod]
    public void ImmediateTerminatorProofDoesNotLeakAcrossUnknownOrNestedCall()
    {
        var child = Variable("abcdefgq");
        foreach (var separator in new[] { Command(62), Command(22, Name("child")) })
        {
            var context = new WorldScriptExecutionContext(new(),
                [new("reserved", 0, 0, -1, 0, 0), new("child", 0, child.Length, -1, 1, 0)], child, true)
                { Mouseovers = new(_ => throw new Exception("Unexpected resource lookup"), (_, _) => { }) };
            var mouseover = Command(94, [4, 10, 0, 0, 0, 20, 0, 0, 0, 0, 0, 0, 0, 0x41, (byte)'c', 0]);
            new WorldScriptExecutor(context).ExecuteStartup(Join(Variable("abcdefgq"), separator, mouseover, Variable("x")));
            Assert.IsNull(context.VariableGlobals.Mutations.Last().Index);
        }
    }

    private static byte[] Variable(string name) => Command(67, Join(Name(name), [11, 0, 0, 0, 0]));
    private static byte[] Name(string name) => Join([1], Encoding.Latin1.GetBytes(name), [0]);
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(value => value).ToArray();
    private static byte[] Command(ushort opcode, byte[]? payload = null)
    {
        var result = new byte[4 + (payload?.Length ?? 0)];
        BinaryPrimitives.WriteUInt16LittleEndian(result, opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(2), (ushort)result.Length);
        payload?.CopyTo(result, 4);
        return result;
    }
}
