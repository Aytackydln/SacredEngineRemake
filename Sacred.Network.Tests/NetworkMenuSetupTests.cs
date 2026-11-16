using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.GameplayHost.Networking;
using Sacred.GameplayHost.World;
using Sacred.Network.Gameplay;

namespace Sacred.Network.Tests;

[TestClass, TestCategory("Packet")]
public sealed class NetworkMenuSetupTests
{
    public static IEnumerable<object[]> Cases() => from source in new uint[] { 3, 4, 6 }
        from result in new byte[] { 0, 1, 0xa5 } select new object[] { source, result };

    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public void EntryInitializationAndFrameRetainActualConnectionResult(uint source, byte result)
    {
        var menu = Memory(new byte[0x180]); var controller = Memory(new byte[0x3ad0]); var page = Memory(new byte[0x400]);
        byte multiplayer = 0; var initialized = false; var ui = 0;
        var services = new WorldNetworkMenuServices(menu, () => multiplayer, () => controller, _ => page,
            (_, _, _) => { }, (_, _) => { }, (_, _) => { });
        var previous = WorldAdmissionControllerSetup.Construct(0);
        previous = previous with { ServiceFlags = previous.ServiceFlags with { Byte381B = 0xa5 } };
        controller.WriteByte(0x381b, 0xa5);
        WorldNetworkMenuEntry.Begin(services, source, () => { }, (current, mode) =>
        {
            initialized = true;
            Assert.AreSame(controller, current);
            Assert.AreEqual((byte)(source == 3 ? 0 : 1), mode);
            var state = WorldAdmissionControllerSetup.Initialize(previous, mode, result, 0);
            multiplayer = state.MultiplayerByte!.Value;
            current.WriteByte(0, state.Gates.ActiveByte0);
            current.WriteByte(0x3818, state.ServiceFlags.Byte3818);
            current.WriteByte(0x381b, state.ServiceFlags.Byte381B);
        });
        Assert.IsTrue(initialized);
        Assert.AreEqual(result, multiplayer);
        Assert.AreEqual((byte)0xa5, controller.ReadByte(0x381b));
        WorldNetworkMenuFrame.Update(new(services, 0, _ => { }, (_, _, _) => { }, _ => ui++), 17);
        Assert.AreEqual(result, multiplayer);
        Assert.AreEqual(result != 0 ? 1 : 0, ui);
        Assert.AreEqual(result == 0 ? source : source == 3 ? 1u : source == 4 ? 2u : 5u, menu.ReadWord(0x154));
        Assert.AreEqual(result == 0 ? (byte)0xa5 : source == 6 ? (byte)1 : (byte)0, controller.ReadByte(0x381b));
        Assert.AreEqual(result, controller.ReadByte(0));
        var owner = new SacredGameplayReplicaSession();
        owner.Apply(GoldGameEventDecoder.Decode(new(0x11, 0, 0, 0, [0, 1, 0, 0, 0, 0])));
        var receipt = owner.Admission.Current!;
        owner.Admission.CaptureCurrentController(receipt, controller, () => page, () => multiplayer);
        Assert.AreEqual(result, receipt.ControllerSnapshot!.MultiplayerByte);
        Assert.AreEqual(controller.ReadByte(0x381b), receipt.ControllerSnapshot.ServiceFlags.Byte381B);
        Assert.AreEqual(result, receipt.ControllerSnapshot.Gates.ActiveByte0);
        Assert.IsFalse(receipt.DeliveryComplete);
        Assert.AreEqual(0, owner.Replicas.Objects.Count);
    }

    [TestMethod]
    public void CaptureKeepsUnknownMultiplayerAndRejectsMissingConnection()
    {
        var controller = Memory(new byte[0x3ad0]); var connection = Memory(new byte[0x50]);
        var capture = WorldAdmissionControllerCapture.Capture(controller, () => connection, () => null);
        Assert.IsNull(capture.MultiplayerByte);
        Assert.ThrowsException<NotSupportedException>(() => WorldAdmissionControllerCapture.Capture(controller, () => null, () => 1));
    }

    private static WorldControlMemory Memory(byte[] bytes) => new(at => bytes[at], (at, value) => bytes[at] = value,
        at => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)at)),
        (at, value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)at), value),
        (at, value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan((int)at), value));
}
