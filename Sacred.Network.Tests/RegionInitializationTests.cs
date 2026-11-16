using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Scripting;
using Sacred.Assets.GameBin;

namespace Sacred.Network.Tests;

[TestClass]
public sealed class RegionInitializationTests
{
    [TestMethod]
    public void UnsupportedEffectAfterSafeCommandsIsRejectedBeforeAnyMutation()
    {
        byte[] code = [62, 0, 4, 0, 21, 0, 4, 0];
        var context = new WorldScriptExecutionContext(new(), [new("", 0, 0, -1, 0, 0), new("region", 0, code.Length, -1, 0, 0)], code, true);
        Assert.IsFalse(WorldScriptRegionCallbackProfile.TryInvoke(context, 1, default, out var blocker));
        StringAssert.Contains(blocker!, "opcode 21");
        Assert.AreEqual(0, context.ExecutedCommands);
        Assert.IsFalse(context.IsInvalidated);
    }

    [TestMethod]
    public void DeferredPackedCallbacksCannotBeSkippedByTheAlreadySetRegionFlag()
    {
        var state = new WorldScriptRegionInitialization();
        uint flags = 0; var writes = 0; var allowSecond = false; var calls = new List<ushort>();
        var services = new WorldScriptRegionInitializationServices(_ => 3, _ => 20, _ => flags,
            (_, mask) => { flags |= mask; writes++; }, _ => 0x00010002, _ => throw new Exception("Packed lookup exists"),
            index => { calls.Add(index); if (index == 1) state.CurrentRegion = 997; return index == 1 || allowSecond; });
        Assert.IsNull(state.TryInitialize(77, true, true, services));
        Assert.AreEqual(1u, flags); Assert.AreEqual(0, state.Records.Count);
        CollectionAssert.AreEqual(new ushort[] { 1, 2 }, calls);
        CollectionAssert.AreEqual(new ushort[] { 2 }, state.PendingFunctions.ToArray());
        Assert.ThrowsException<InvalidOperationException>(() => state.TryInitialize(77, true, true, services));
        Assert.ThrowsException<InvalidOperationException>(() => state.TryInitialize(88, true, true, services));
        allowSecond = true;
        Assert.AreEqual(true, state.Continue(services));
        CollectionAssert.AreEqual(new ushort[] { 1, 2, 2 }, calls);
        Assert.AreEqual(1, writes); Assert.AreEqual((997u, 1u), state.Records[997]);
        Assert.AreEqual(uint.MaxValue, state.CurrentRegion);
        Assert.ThrowsException<InvalidOperationException>(() => state.Continue(services));
    }
}
