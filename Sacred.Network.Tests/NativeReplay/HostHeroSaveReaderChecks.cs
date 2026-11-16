using System.Buffers.Binary;
using System.Text.Json;
using System.Runtime.InteropServices;
using Sacred.Core.CombatArts;
using Sacred.Core.Pax.Hero.Objects;

/// <summary>Original full-reader source/layout and model/animation request
/// boundaries. Derived normalization is observed, not implemented by this test.</summary>
internal static class HostHeroSaveReaderChecks
{
    public static int Run(string root)
    {
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-hero-save-reader.json")));
        var data = proof.RootElement;
        var sourceBytes = Convert.FromHexString(data.GetProperty("source_hex").GetString()!);
        var source = SacredSavedCreature.FromPayload(sourceBytes, data.GetProperty("format_version").GetByte());
        var actor = Convert.FromHexString(data.GetProperty("actor_hex").GetString()!);
        var checks = 0;
        Check(source.SerializedSize == data.GetProperty("consumed").GetInt32(), "complete creature consumed");
        Equal(source.PhysicalData.Slice(SacredSavedPhysicalLayout.Vector70Offset, 12), actor.AsSpan(0x70, 12), "direction");
        Equal(sourceBytes.AsSpan(SacredSavedCreatureLayout.BlockFCOffset, 84), actor.AsSpan(0xfc, 84), "KI bank");
        Equal(sourceBytes.AsSpan(SacredSavedCreatureLayout.Block150Offset, 84), actor.AsSpan(0x150, 84), "EI bank");
        Equal(source.CreaturePrefix.Slice(SacredSavedCreatureLayout.Block1F0Offset - SacredSavedCreatureLayout.MarkerOffset, 253), actor.AsSpan(0x1f0, 253), "current-format actor prefix");
        Equal(sourceBytes.AsSpan(SacredSavedCreatureLayout.Block2EDOffset, 173), actor.AsSpan(0x2ed, 173), "action bank");
        Equal(source.Values.Block128, actor.AsSpan(0x4d0, 24), "health input");
        for (var index = 0; index < SacredSavedCreatureLayout.EquipmentCount; index++)
            Check(source.GetEquipmentReference(index) == BinaryPrimitives.ReadUInt32LittleEndian(actor.AsSpan(0x1a4 + index * 4)), "file equipment reference before outer rebase");
        var arts = Convert.FromHexString(data.GetProperty("arts_hex").GetString()!);
        var combos = Convert.FromHexString(data.GetProperty("combos_hex").GetString()!);
        Check(arts.Length == source.Values.Count22 * 22 && combos.Length == source.Values.Count38 * 38, "replaced original art/combo list lengths");
        var changedTimers = 0;
        for (var index = 0; index < source.Values.Count22; index++)
        {
            var retained = source.Values.GetCombatArt(index);
            var native = MemoryMarshal.Read<SacredCombatArtRuntimeLayout>(arts.AsSpan(index * 22, 22));
            Equal(source.Values.GetRecord22(index)[..10], arts.AsSpan(index * 22, 10), "art kind/code/ranks/flags retained");
            Check(retained.TimerFactor == native.TimerFactor && retained.Timer == native.Timer, "art factor/elapsed retained");
            Check(float.IsFinite(native.TimerMaximum) && native.TimerMaximum > 0, "native recalculated art timer");
            if (retained.TimerMaximum != native.TimerMaximum) changedTimers++;
        }
        Check(changedTimers != 0, "local-hero load recalculates art TimerMaximum; packet projection is insufficient");
        for (var index = 0; index < source.Values.Count38; index++) Equal(source.Values.GetRecord38(index), combos.AsSpan(index * 38, 38), "combo record");
        Check(!source.Values.Block08.SequenceEqual(actor.AsSpan(0x3b0, 238)), "native load normalization differs from saved derived stats");
        var request = SacredSavedAnimationRequest.Read(source.PhysicalData);
        var parameters = data.GetProperty("parameters").EnumerateArray().ToArray();
        Check(parameters.Length == 5, "old control setters precede activation/new control setters");
        Check(parameters[^2][0].GetInt32() == 3 && parameters[^2][1].GetSingle() == request.FirstArgument, "new control loop-count argument");
        Check(parameters[^1][0].GetInt32() == 2 && parameters[^1][1].GetSingle() == request.SecondArgument, "new control speed argument with original current factors");
        Check(parameters[2][0].GetInt32() == 0 && parameters[2][1].GetDouble() == BinaryPrimitives.ReadDoubleLittleEndian(source.PhysicalData[SacredSavedPhysicalLayout.AnimationControlTimeDoubleOffset..]), "third saved double goes to previous control before activation");
        return checks;
        void Check(bool pass, string reason) { if (!pass) throw new InvalidDataException("HostHeroSaveReader: " + reason); checks++; }
        void Equal(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, string reason) => Check(actual.SequenceEqual(expected), reason);
    }
}
