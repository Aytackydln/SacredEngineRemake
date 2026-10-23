using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

/// <summary>Existing native-result comparisons, promoted out of disposable scratch projects.</summary>
[TestClass, TestCategory("Replay")]
public sealed class NativeReplayTests
{
    private static readonly IReadOnlyDictionary<string, Func<string, int>> Suites = new Dictionary<string, Func<string, int>>
    {
        ["LobbyUpdates 0x0027 0x002D 0x002E 0x002F 0x0031"] = LobbyUpdateChecks.Run,
        ["SessionNotices 0x0035 0x0038 0x003A 0x003C 0x003D"] = SessionNoticeChecks.Run,
        ["ChannelChatHostRole 0x0003 0x0004 0x0005 0x000D 0x000E"] = ChannelChatHostRoleChecks.Run,
        ["SessionText 0x0032 0x01A8"] = SessionTextChecks.Run,
        ["ScriptGroups 0x018F"] = ScriptGroupChecks.Run,
        ["InactiveReceiveHooks 0x018E 0x0190 0x0191 0x0192 0x0193 0x0194 0x0195"] = InactiveReceiveHookChecks.Run,
        ["ObjectCallbackCleanup 0x01C1"] = ObjectCallbackCleanupChecks.Run,
        ["SessionPause 0x0134 0x0135"] = SessionPauseChecks.Run,
        ["Teams 0x0138 0x0139 0x013A 0x013B"] = TeamChecks.Run,
        ["SessionPlayerLimit 0x0023"] = SessionPlayerLimitChecks.Run,
        ["SessionGameFlags 0x001E"] = SessionGameFlagsChecks.Run,
        ["SessionLocks 0x0019 0x001A"] = SessionLockChecks.Run,
        ["TriggerPatches 0x01CE"] = TriggerPatchChecks.Run,
        ["ScriptRegions 0x01C5"] = ScriptRegionChecks.Run,
        ["MovementTriggerInsertion 0x01C3"] = MovementTriggerInsertionChecks.Run,
        ["MovementCallbacks 0x01D1"] = MovementCallbackChecks.Run,
        ["ScriptDefinitions 0x01BF 0x01C0"] = ScriptDefinitionChecks.Run,
        ["ScriptFunctions 0x01C4"] = ScriptFunctionChecks.Run,
        ["PositionCorrections 0x0179"] = PositionCorrectionChecks.Run,
        ["ScriptTables 0x01D3 0x01D4 0x01D5"] = ScriptTableChecks.Run,
        ["ScriptRegistrations 0x01CF 0x01D0"] = ScriptRegistrationChecks.Run,
        ["SectorPatches"] = SectorPatchChecks.Run,
        ["ActualQuests 0x01C9 0x01CA"] = ActualQuestsChecks.Run,
        ["ScriptSpatial 0x01CB 0x01CC"] = ScriptSpatialChecks.Run,
        ["RegionValues 0x0187 0x0188"] = RegionValueChecks.Run,
        ["ObjectQuest 0x0173 0x0175"] = ObjectQuestChecks.Run,
        ["CreatureSleep 0x0126"] = CreatureSleepChecks.Run,
        ["Calendar"] = CalendarChecks.Run,
        ["PhysicalFacing"] = PhysicalFacingChecks.Run,
        ["ObjectLifecycle"] = ObjectLifecycleChecks.Run,
        ["ScriptResources"] = ScriptResourceChecks.Run,
        ["References"] = ReferenceChecks.Run,
        ["CreatureValues"] = CreatureValuesChecks.Run,
        ["CreatureState"] = CreatureStateChecks.Run,
        ["ObjectState"] = ObjectStateChecks.Run,
        ["InventoryMembership"] = InventoryMembershipChecks.Run,
        ["EquipmentAssignment"] = EquipmentAssignmentChecks.Run,
        ["RemainingWorldBanks"] = RemainingWorldBankChecks.Run,
        ["WorldStartup"] = WorldStartupChecks.Run,
        ["WorldTriggers"] = WorldTriggerChecks.Run
    };
    public TestContext TestContext { get; set; } = null!;
    public static IEnumerable<object[]> Cases() => Suites.Keys.Select(name => new object[] { name });
    [TestMethod, DynamicData(nameof(Cases), DynamicDataSourceType.Method)]
    public void MatchesNativeEvidence(string suite)
    {
        var checks = Suites[suite](RepositoryFiles.Root);
        Assert.IsTrue(checks > 0, suite + " executed no assertions");
        TestContext.WriteLine($"{suite}: {checks} native-result assertions");
    }
}
