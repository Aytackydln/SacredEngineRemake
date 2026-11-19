using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;

internal static class HostEnteredNpcGateChecks
{
    public static int Run(string root)
    {
        using var proof = HostSectorInitializationChecks.Read(root,"native-host-entered-npc-gate");
        var creation = SacredScriptCreatureCreationReader.Read(new(0x2aa6be,HostSectorInitializationChecks.Bytes(proof.RootElement,"command_hex")),
            _ => throw new Exception("Reached command must have literal placement."));
        var checks = 0;
        Check(!SacredScriptCreatureEntryPolicy.SkipMultiplayerEntry(creation,true,uint.MaxValue,1) && creation.HasExplicitBehavior && creation.Behavior == 0x4000,
            "actual explicit behavior bypasses the native setting-1 early return");
        var descriptors = proof.RootElement.GetProperty("descriptors");
        var categories = creation.EquipmentTypes.Select(resource => MemoryMarshal.Read<ItemsPakEntryModelDescLayout>(
            Convert.FromHexString(descriptors.GetProperty(resource.ToString()).GetString()!)).Category).ToArray();
        Check(categories.SequenceEqual(new[] { SacredItemCategory.Shield,SacredItemCategory.Weapon }), "authored equipment requires Shield then Weapon, in source order");
        foreach (var row in proof.RootElement.GetProperty("cases").EnumerateArray())
        {
            var call = row.GetProperty("factory"); var position = HostSectorInitializationChecks.Bytes(call,"position_hex");
            Check(call.GetProperty("entry").GetString() == "0x5fba40" && call.GetProperty("args")[0].GetUInt32() == creation.TypeId &&
                (call.GetProperty("args")[4].GetUInt32() & 255) == 1, "actual loaded positioned factory and low-byte owned argument");
            Check(creation.WorldPosition!.Value.X == BinaryPrimitives.ReadInt32LittleEndian(position.AsSpan(4)) &&
                creation.WorldPosition.Value.Y == BinaryPrimitives.ReadInt32LittleEndian(position.AsSpan(8)) &&
                BinaryPrimitives.ReadUInt16LittleEndian(position) == 2503 && position[12] == 0,
                "actual world-position precedence and original grid select second requested sector");
            var random = new SacredRandomSequence(row.GetProperty("seed").GetUInt32()); random.Next();
            Check(row.GetProperty("draws").GetInt32() == 1 && random.State == row.GetProperty("rng_after").GetUInt32(),
                "one predecode facing draw precedes the first factory, without claiming completed construction");
        }
        return checks;
        void Check(bool value,string reason) { if (!value) throw new Exception("Entered NPC gate: " + reason); checks++; }
    }
}
