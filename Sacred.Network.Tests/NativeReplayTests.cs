using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

/// <summary>Existing native-result comparisons, promoted out of disposable scratch projects.</summary>
[TestClass, TestCategory("Replay")]
public sealed class NativeReplayTests
{
    private static readonly IReadOnlyDictionary<string, Func<string, int>> Suites = new Dictionary<string, Func<string, int>>
    {
        ["CreatureLevelSelection"] = CreatureLevelSelectionChecks.Run,
        ["CreatureComponentSeed"] = CreatureComponentSeedChecks.Run,
        ["CreatureResultInitialization"] = CreatureResultInitializationChecks.Run,
        ["CreatureBaseResults"] = CreatureBaseResultChecks.Run,
        ["CreaturePassiveSkills"] = CreaturePassiveSkillChecks.Run,
        ["CreatureEquipmentResults"] = CreatureEquipmentResultChecks.Run,
        ["CreatureInnateBonuses"] = CreatureInnateBonusChecks.Run,
        ["EquipmentZeroQuality"] = EquipmentZeroQualityChecks.Run,
        ["EquipmentNormalization"] = EquipmentNormalizationChecks.Run,
        ["EquipmentPricing"] = EquipmentPricingChecks.Run,
        ["EquipmentSlotSelection"] = EquipmentSlotSelectionChecks.Run,
        ["EquipmentInstallation"] = EquipmentInstallationChecks.Run,
        ["EquipmentVariantNormalization"] = EquipmentVariantNormalizationChecks.Run,
        ["EquipmentSelection"] = EquipmentSelectionChecks.Run,
        ["EquipmentModifierLookup"] = EquipmentModifierLookupChecks.Run,
        ["EquipmentSelectionPool"] = EquipmentSelectionPoolChecks.Run,
        ["EquipmentResourceNames"] = EquipmentResourceNameChecks.Run,
        ["EquipmentClassInference"] = EquipmentClassInferenceChecks.Run,
        ["EquipmentVariants"] = EquipmentVariantChecks.Run,
        ["EquipmentFactory"] = EquipmentFactoryChecks.Run,
        ["ItemCategorySelection"] = ItemCategorySelectionChecks.Run,
        ["EquipmentMagic"] = EquipmentMagicChecks.Run,
        ["LootRarity"] = LootRarityChecks.Run,
        ["CreatureLootEvent"] = CreatureLootEventChecks.Run,
        ["CreatureLootDecisionSession"] = CreatureLootDecisionSessionChecks.Run,
        ["CreatureLootEntry"] = CreatureLootEntryChecks.Run,
        ["CreatureDeathRewards"] = CreatureDeathRewardChecks.Run,
        ["EquipmentReward"] = EquipmentRewardChecks.Run,
        ["EquipmentLiveSelection"] = EquipmentLiveSelectionChecks.Run,
        ["CreatureLootGeneration"] = CreatureLootGenerationChecks.Run,
        ["CreatureLootUnique"] = CreatureLootUniqueChecks.Run,
        ["CreatureLootSets"] = CreatureLootSetChecks.Run,
        ["EquipmentFit"] = EquipmentFitChecks.Run,
        ["LootSpecialResources"] = LootSpecialResourceChecks.Run,
        ["CreatureDeathPrefix"] = CreatureDeathPrefixChecks.Run,
        ["LootSimulator"] = LootSimulatorChecks.Run,
        ["EquipmentNames"] = EquipmentNameChecks.Run,
        ["CombatStartup 0x001E 0x017A"] = CombatStartupChecks.Run,
        ["CombatVisuals 0x017A 0x0128 0x011B 0x0119 0x001E"] = CombatVisualChecks.Run,
        ["HealthVisibility 0x0119 0x011A 0x0128 0x017A"] = HealthVisibilityChecks.Run,
        ["ConsumerPresentation 0x011F 0x013D 0x017A 0x017E 0x0182 0x0184 0x019E 0x019F 0x01A0 0x01A1 0x01A3"] = ConsumerPresentationChecks.Run,
        ["RemainingReceives 0x0127 0x0132 0x014B 0x016A 0x0181 0x0197 0x0198 0x0199 0x019B 0x01AA 0x01AB 0x01AC"] = RemainingReceiveChecks.Run,
        ["LobbyLifecycle 0x0002 0x0009 0x000C 0x001C 0x0024 0x0026 0x0029 0x002B 0x0150 0x0151"] = LobbyLifecycleChecks.Run,
        ["CreatureInteractions 0x0033 0x0036 0x0143 0x0145 0x014A 0x014D 0x014E 0x017D 0x0189 0x018B"] = CreatureInteractionChecks.Run,
        ["CreatureDeath 0x017D 0x014E"] = CreatureDeathChecks.Run,
        ["CreatureCommands 0x0114 0x0125 0x012E 0x012F 0x013C 0x015E 0x0160 0x0177 0x0180 0x01CD"] = CreatureCommandChecks.Run,
        ["ReceiveActions 0x0110 0x013D 0x0178 0x017E 0x01D2"] = ReceiveActionChecks.Run,
        ["CounterReceives 0x019E 0x019F 0x01A0 0x01A1 0x01A2 0x01A3"] = CounterReceiveChecks.Run,
        ["SpecialFxReceives 0x01A4 0x01A5 0x01A6 0x01A9"] = SpecialFxReceiveChecks.Run,
        ["QuestDeltas 0x01B8 0x01B9 0x01BA 0x01BB 0x01BC 0x01BD 0x01BE 0x015D 0x0170 0x018D"] = QuestDeltaChecks.Run,
        ["SessionBrowser 0x0001 0x0008 0x001F 0x0020 0x002A 0x002C 0x0030 0x0037 0x0039 0x0047"] = SessionBrowserChecks.Run,
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
        ["SeraphimVisuals 0x0183 0x016F"] = SeraphimVisualChecks.Run,
        ["VampireTransformation 0x012D"] = VampireTransformationChecks.Run,
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
