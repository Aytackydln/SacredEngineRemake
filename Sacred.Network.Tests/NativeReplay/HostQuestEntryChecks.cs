using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostQuestEntryChecks
{
    public static int Run(string root)
    {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-quest-entry.json")));
        var checks = 0;
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var before = Bytes(row, "before_hex"); var after = Bytes(row, "after_hex");
            var basis = new GoldObjectBaseState(Word(before, 12), Word(before, 16), Word(before, 20), Short(before, 24),
                Int(before, 28), Int(before, 32), before[36], Word(before, 40), Word(before, 52), before[56], before[57], Word(before, 60));
            var actor = new WorldCreatureInitialActor(basis, new(before.AsSpan(0x3b0, 238).ToArray(), new byte[24], 0, 0), 0, 0, 0, 0, 0, 0, [])
                { KiState = before.AsSpan(0xfc,84).ToArray(), ScriptInitialPosition = new(Int(before, 0x2bd), Int(before, 0x2c1), before[0x2c5], Short(before, 0x2b9)) };
            var nativeQuest = Bytes(row, "quest_hex"); var initialQuest = nativeQuest.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(initialQuest.AsSpan(288), 1);
            var quests = new WorldScriptQuests(new[] { SacredScriptQuestDefinition.FromPayload(new byte[292]), SacredScriptQuestDefinition.FromPayload(initialQuest) })
            { PrepareEnteredHero = number => actor = WorldScriptEnteredQuestHero.Prepare(actor, number, row.GetProperty("flags").GetUInt32(), row.GetProperty("active").GetBoolean()) };
            // Native fixture explicitly clears all pending compass words before entry.
            quests.Execute(new(0, Convert.FromHexString("15000400")), true, _ => { });
            var called = 0;
            WorldScriptQuestTrigger.Enter(quests, 1, 1, true, index =>
            {
                Check(quests.Get(1).Flags == 5 && quests.Journal.Count == 1, "journal and triggered flag precede OnEnter");
                called = index;
            });
            var actual = before.ToArray(); var position = actor.ScriptInitialPosition;
            BinaryPrimitives.WriteUInt16LittleEndian(actual.AsSpan(0x2b9), checked((ushort)position.Sector));
            BinaryPrimitives.WriteInt32LittleEndian(actual.AsSpan(0x2bd), position.X);
            BinaryPrimitives.WriteInt32LittleEndian(actual.AsSpan(0x2c1), position.Y); actual[0x2c5] = checked((byte)position.Surface);
            Check(actual.AsSpan().SequenceEqual(after), "full hero preservation and checkpoint bytes, including padding");
            Check(quests.Get(1).ToPayload().AsSpan().SequenceEqual(nativeQuest), "complete original quest row");
            Check(quests.Journal.GetRecord(0).AsSpan().SequenceEqual(Bytes(row, "journal_hex")) &&
                quests.Journal.GetIndices().AsSpan().SequenceEqual(Bytes(row, "indices_hex")), "complete original journal/index state");
            Check(called == quests.Get(1).EnterFunction, "actual final callback");
        }
        return checks;
        void Check(bool condition, string reason) { if (!condition) throw new Exception("Host quest entry: " + reason); checks++; }
    }
    private static byte[] Bytes(JsonElement row, string key) => Convert.FromHexString(row.GetProperty(key).GetString()!);
    private static uint Word(byte[] bytes, int at) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static int Int(byte[] bytes, int at) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Short(byte[] bytes, int at) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
}
