using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class HostCharacterWorldEntryChecks
{
    public static int Run(string root)
    {
        using var proof = Read("native-host-character-world-entry");
        using var heroProof = Read("native-host-hero-post-load");
        var source = SacredSavedCreature.FromPayload(Bytes(heroProof.RootElement, "source_hex"), heroProof.RootElement.GetProperty("format_version").GetByte());
        var checks = 0;
        var world = proof.RootElement.GetProperty("world");
        var before = Bytes(world, "before_hex");
        var expected = Bytes(world, "actor_hex");
        var actor = Actor(before);
        var first = WorldCreatureHostWorldPrelude.BeginFreshWorld(actor);
        var oldSector = proof.RootElement.GetProperty("sectors")[0];
        var keyx = MemoryMarshal.Read<KeyxSectorRecord>(Bytes(oldSector, "keyx_hex"));
        var tileIndex = (SacredWorldCoordinates.WorldToTile(actor.Base.Y) - SacredSectorCoordinates.ToTileOrigin(keyx.RawY)) * 64 +
                        SacredWorldCoordinates.WorldToTile(actor.Base.X) - SacredSectorCoordinates.ToTileOrigin(keyx.RawX);
        var tile = WldxTileRecord.FromBytes(Bytes(oldSector, "tiles_hex").AsSpan(tileIndex * 32));
        var detached = first with { Base = first.Base with { Flags = first.Base.Flags | 2u, Sector = 0 }, NextOnTile = 0 };
        var placed = WorldCreatureHostGroundPlacement.Place(detached, new(actor.Base.X, actor.Base.Y, 0, actor.Base.Sector), tile, 0);
        var final = WorldCreatureHostWorldPrelude.CompleteOutdoor(placed);
        Check(final.Flags1FC == Short(expected, 0x1fc) && final.SavedWorldPreludeCompleted, "fresh-world native flag/action state");
        Check(final.Base == Base(expected) && ReferenceEquals(final.Node, actor.Node) && final.Node.Active, "native local-hero reinsertion preserves current model/node/identity");
        Check(final.EquipmentReferences.SequenceEqual(Enumerable.Range(0,19).Select(index=>Word(expected,0x1a4+index*4))), "parameter9 actual slot18 clear");
        Check(final.RestoredPhysical!.PaletteMask == Word(expected,0x5c), "parameter9 palette clear");
        var membership = new WorldCreatureSectorMembership();
        membership.Move(final.Base.ObjectReference,0,final.Base.Sector);
        Check(membership.Get(final.Base.Sector).SequenceEqual(world.GetProperty("members").EnumerateArray().Select(value=>value.GetUInt32())), "real local hero admitted to existing sector membership");

        foreach(var inventory in proof.RootElement.GetProperty("refresh").GetProperty("inventories").EnumerateArray())
        {
            var original = Bytes(inventory,"payload_before_hex");
            var saved = SacredSavedInventory.FromPayload(original);
            var result = inventory.GetProperty("id").GetInt32()==1 ? GoldSavedInventoryQuestReset.Apply(saved) : saved;
            Check(result.ToPayload().SequenceEqual(Bytes(inventory,"payload_hex")), "actual primary quest-vector reset, fixed fields and scrolls preserved");
            Check(saved.ToPayload().SequenceEqual(original), "immutable saved source retained");
            if(inventory.GetProperty("id").GetInt32()==1)
                Check(result.QuestCount==0 && ReferenceEquals(result,GoldSavedInventoryQuestReset.Apply(result)), "quest reset idempotence");
        }
        var refresh = proof.RootElement.GetProperty("refresh");
        Check(Bytes(refresh,"before_hex").SequenceEqual(Bytes(refresh,"actor_hex")) &&
              refresh.GetProperty("items").EnumerateArray().All(row=>row.GetProperty("before_hex").GetString()==row.GetProperty("item_hex").GetString()),
            "native quest reset does not recalculate hero or items");

        var entry = proof.RootElement.GetProperty("entry");
        var entryActor = Actor(Bytes(entry,"before_hex"));
        var positionBytes = Bytes(entry,"position_hex");
        var position = new GoldObjectPosition(unchecked((int)Word(positionBytes,4)),unchecked((int)Word(positionBytes,8)),positionBytes[12],Short(positionBytes,0));
        var entrySector = proof.RootElement.GetProperty("sectors")[1];
        var entryKeyx = MemoryMarshal.Read<KeyxSectorRecord>(Bytes(entrySector,"keyx_hex"));
        var entryIndex = (SacredWorldCoordinates.WorldToTile(position.Y)-SacredSectorCoordinates.ToTileOrigin(entryKeyx.RawY))*64+
                         SacredWorldCoordinates.WorldToTile(position.X)-SacredSectorCoordinates.ToTileOrigin(entryKeyx.RawX);
        var entryTile = WldxTileRecord.FromBytes(Bytes(entrySector,"tiles_hex").AsSpan(entryIndex*32));
        var retired = entryActor with { Base=entryActor.Base with { Flags=entryActor.Base.Flags|2u },NextOnTile=0 };
        var moved = WorldCreatureHostGroundPlacement.Place(retired,position,entryTile,0);
        Check(moved.Base==Base(Bytes(entry,"actor_hex")) && ReferenceEquals(moved.RestoredPhysical,entryActor.RestoredPhysical), "native campaign logical position, unchanged physical/control state");
        membership.Move(moved.Base.ObjectReference,entryActor.Base.Sector,moved.Base.Sector);
        Check(membership.Get(entryActor.Base.Sector).Count==0 && membership.Get(moved.Base.Sector).SequenceEqual(entry.GetProperty("members").EnumerateArray().Select(value=>value.GetUInt32())), "native membership move with local identity");
        using var reload = Read("native-host-initial-sector-reload");
        var reloadBytes = Bytes(reload.RootElement,"before_hex");
        var pending = Actor(reloadBytes) with
        {
            KiState=reloadBytes.AsSpan(0xfc,84).ToArray(), EiState=reloadBytes.AsSpan(0x150,84).ToArray(),
            DialogIndex=Word(reloadBytes,0x245), RuntimeFlags=Word(reloadBytes,0x200)
        };
        var reinserted = WorldCreatureHostGroundPlacement.Place(pending with
            { Base=pending.Base with { Flags=pending.Base.Flags|2u }, NextOnTile=0 },position,entryTile,0);
        Check(reinserted.Base==Base(Bytes(reload.RootElement,"after_hex")) && reinserted.NextOnTile==Word(reloadBytes,0x30),
            "native initial-sector reload retains entered position and chain");
        Check(reinserted.KiState.SequenceEqual(reloadBytes.AsSpan(0xfc,84).ToArray()) &&
              reinserted.EiState.SequenceEqual(reloadBytes.AsSpan(0x150,84).ToArray()) && reinserted.DialogIndex==Word(reloadBytes,0x245) &&
              reinserted.RuntimeFlags==Word(reloadBytes,0x200), "pending POPUP mode10/state5 survives real reload");
        Check(ReferenceEquals(reinserted.Node,pending.Node) && ReferenceEquals(reinserted.RestoredPhysical,pending.RestoredPhysical) &&
              ReferenceEquals(reinserted.EquipmentReferences,pending.EquipmentReferences) && ReferenceEquals(reinserted.Component,pending.Component),
            "same model, activation, equipment and current values survive reload");
        Check(reloadBytes.SequenceEqual(Bytes(reload.RootElement,"after_hex")),"complete native hero unchanged by reload");
        try { WorldCreatureHostGroundPlacement.Place(retired with { Base=retired.Base with { Owner=0 } },position,entryTile,0);throw new Exception("Unowned local hero accepted"); }
        catch(NotSupportedException) { checks++; }
        return checks;

        WorldCreatureInitialActor Actor(byte[] bytes)
        {
            var result = new WorldCreatureInitialActor(Base(bytes),new(bytes.AsSpan(0x3b0,238).ToArray(),bytes.AsSpan(0x4d0,24).ToArray(),bytes[0x4ce],bytes[0x4cf]),
                Word(bytes,0x1f0),Word(bytes,0x1f4),0,0,0,0,[])
            {
                HeroPostLoadCompleted=true,SavedEquipmentReferencesBound=true,Flags1FC=Short(bytes,0x1fc),
                RestoredCreature=new(source,[]),EquipmentReferences=Enumerable.Range(0,19).Select(index=>Word(bytes,0x1a4+index*4)).ToArray(),
                RestoredPhysical=new(Word(bytes,0x64),0,0,0,0,Word(bytes,0x5c),default,default,default,default,default,0,0,Word(bytes,0xb0),default,default,default,null,0,0)
            };
            new WorldObjectActivationQueue().ActivateInitial(result.Node,1);
            return result;
        }
        void Check(bool condition,string reason) { if(!condition)throw new Exception("Host world entry: "+reason);checks++; }
        JsonDocument Read(string name)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence",name+".json")));
    }
    private static GoldObjectBaseState Base(byte[] bytes)=>new(Word(bytes,12),Word(bytes,16),Word(bytes,20),Short(bytes,24),
        unchecked((int)Word(bytes,28)),unchecked((int)Word(bytes,32)),bytes[36],Word(bytes,40),Word(bytes,52),bytes[56],bytes[57],Word(bytes,60));
    private static uint Word(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at));
    private static ushort Short(byte[] bytes,int at)=>BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(at));
    private static byte[] Bytes(JsonElement row,string name)=>Convert.FromHexString(row.GetProperty(name).GetString()!);
}
