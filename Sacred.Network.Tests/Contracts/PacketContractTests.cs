using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Packet")]
public sealed class PacketContractTests
{
    public static IEnumerable<object[]> Cases() => PacketCatalog.Rows();

    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public void DecodesExpectedEventAndFields(string id)
    {
        var packet = PacketCatalog.Get(id); var entry = GoldGameEventDecoder.Decode(packet.Message());
        Assert.AreEqual(packet.EventType, entry.GetType(), id); packet.Verify?.Invoke(entry);
    }

    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public void PayloadLengthAndAtomicDispatch(string id)
    {
        var packet = PacketCatalog.Get(id); var message = packet.Message();
        for (var size = 0; size < message.Payload.Length; size++)
        {
            if (packet.ValidShorterLengths?.Contains(size) == true) continue;
            var bad = message with { Payload = message.Payload[..size] }; var delivered = 0;
            Assert.ThrowsException<InvalidDataException>(() => GoldGameDispatcher.Dispatch(
                [message, bad], _ => delivered++), $"{id}, truncated to {size} bytes");
            Assert.AreEqual(0, delivered, $"{id}: partial dispatch at {size}");
        }
        var suffix = message with { Payload = [..message.Payload, 0xa5] };
        if (packet.Rule == PayloadRule.Exact)
            Assert.ThrowsException<InvalidDataException>(() => GoldGameEventDecoder.Decode(suffix), id + ": extra byte");
        else if (packet.Rule == PayloadRule.RetainedSuffix)
        {
            var entry = GoldGameEventDecoder.Decode(suffix);
            Assert.AreEqual(packet.EventType, entry.GetType(), id);
            CollectionAssert.AreEqual(suffix.Payload, entry.Message.Payload, id + ": suffix lost");
        }
    }

    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public void WireEnvelopePreservesPayloadIdentityAndSequence(string id)
    {
        var packet = PacketCatalog.Get(id);
        foreach (var sequence in new ushort[] { 0, ushort.MaxValue })
        {
            var message = packet.Message() with { Sequence = sequence };
            var wire = GoldEnvelopeCodec.Encode(GoldMessageCodec.Encode(message));
            var decoded = GoldMessageCodec.DecodeBatch(GoldEnvelopeCodec.Decode(wire)).Single();
            Assert.AreEqual(message.Opcode, decoded.Opcode, id); Assert.AreEqual(message.Sender, decoded.Sender, id);
            Assert.AreEqual(message.Origin, decoded.Origin, id); Assert.AreEqual(sequence, decoded.Sequence, id);
            CollectionAssert.AreEqual(message.Payload, decoded.Payload, id);
        }
    }

    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public void OwnedPayloadSurvivesCallerMutation(string id)
    {
        var packet = PacketCatalog.Get(id); var message = packet.Message(); var expected = message.Payload.ToArray();
        var entry = GoldGameEventDecoder.Decode(message); Array.Fill(message.Payload, (byte)0xa5);
        packet.Verify?.Invoke(entry);
        if (packet.OwnsMessage) CollectionAssert.AreEqual(expected, entry.Message.Payload, id);
    }
}
