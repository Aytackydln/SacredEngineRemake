using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests.Contracts;

[TestClass, TestCategory("Infrastructure")]
public sealed class EventCoverageTests
{
    [TestMethod]
    public void EveryConcreteEventInGoldGameEventFileHasAContractExample()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "Sacred.Network", "Gameplay", "GoldGameEvent.cs"));
        var types = Regex.Matches(source, @"public sealed record (\w+)").Select(m => m.Groups[1].Value);
        var covered = PacketCatalog.All.Select(row => row.EventType.Name).ToHashSet();
        var missing = types.Where(type => !covered.Contains(type)).ToArray();
        Assert.AreEqual(0, missing.Length, "Add PacketCatalog cases for: " + string.Join(", ", missing));
        Assert.AreEqual(PacketCatalog.All.Count, PacketCatalog.All.Select(row => row.Id).Distinct().Count(), "Duplicate packet case IDs.");
    }
}
