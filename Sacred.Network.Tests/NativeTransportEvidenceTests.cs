using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Protocol;
using Sacred.Network.Tests.Infrastructure;
using Sacred.Network.TinCat;

namespace Sacred.Network.Tests;

/// <summary>Independent native bytes catch encoder/decoder bugs that could cancel out in a round trip.</summary>
[TestClass, TestCategory("Replay")]
public sealed class NativeTransportEvidenceTests
{
    [TestMethod]
    public void SacredChecksumMatchesOriginalInstructionResults()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("native-replay.json")));
        var samples = fixture.RootElement.GetProperty("checksum_samples").EnumerateArray().ToArray();
        Assert.IsTrue(samples.Length > 0);
        foreach (var sample in samples)
        {
            var bytes = Convert.FromHexString(sample.GetProperty("bytes").GetString()!);
            var expected = Convert.ToUInt32(sample.GetProperty("checksum").GetString()![2..], 16);
            Assert.AreEqual(expected, SacredChecksum.Compute(bytes), Convert.ToHexString(bytes));
        }
    }

    [TestMethod]
    public void TinCatLoginMatchesOriginalBytes()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("tincat/native-login.json")));
        var request = Convert.FromHexString(fixture.RootElement.GetProperty("request_hex").GetString()!);
        var response = Convert.FromHexString(fixture.RootElement.GetProperty("response_hex").GetString()!);
        CollectionAssert.AreEqual(request, TinCatFrameCodec.Encode(TinCatLogin.CreateRequest("user")));
        Assert.AreEqual(1u, TinCatLogin.ReadAccepted(TinCatFrameCodec.Decode(response)));
    }

    [TestMethod]
    public void CorruptedNativeTinCatFrameIsRejected()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(RepositoryFiles.Evidence("tincat/native-login.json")));
        var response = Convert.FromHexString(fixture.RootElement.GetProperty("response_hex").GetString()!);
        response[^1] ^= 1;
        Assert.ThrowsException<InvalidDataException>(() => TinCatFrameCodec.Decode(response));
    }
}
