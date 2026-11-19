using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Gameplay;

internal static class HostCurrentPublicationChecks
{
    public static int Run(string root)
    {
        using var proof = Read(root);
        var data = proof.RootElement;
        var chunks = data.GetProperty("definition_chunks").EnumerateArray().ToArray();
        var image = new byte[chunks.Max(row=>row.GetProperty("offset").GetInt32()+row.GetProperty("hex").GetString()!.Length/2)];
        foreach(var row in chunks) Bytes(row,"hex").CopyTo(image,row.GetProperty("offset").GetInt32());
        var defaults = SacredHostPublicationDefaults.Read(image);
        var clock = Bytes(data.GetProperty("defaults"),"clock_hex");
        var checks=0;
        Check(defaults.Hour==BinaryPrimitives.ReadUInt16LittleEndian(clock.AsSpan(0x20)) && defaults.Minute==BinaryPrimitives.ReadUInt16LittleEndian(clock.AsSpan(0x22)), "native fresh clock inputs");
        Check(defaults.DayStart==data.GetProperty("defaults").GetProperty("day_start").GetUInt16() && defaults.DayEnd==data.GetProperty("defaults").GetProperty("day_end").GetUInt16(), "native day interval");
        foreach(var row in data.GetProperty("mode_controls").EnumerateArray())
            Check(defaults.Mode(row.GetProperty("hour").GetUInt16(),row.GetProperty("minute").GetUInt16(),row.GetProperty("forced").GetBoolean())==row.GetProperty("mode").GetByte(), "actual native hero mode policy");
        var hero = data.GetProperty("publications")[0];
        var state = GoldCreatureState.FromPayload(Bytes(hero,"payload_hex"));
        Check(state.ReadTail(0x169)!.Mode==defaults.HeroMode && state.BaseState.Sector==2581 && state.BaseState.ObjectReference==1, "current hero mode/entry identity");
        var header = GoldObjectStateCodec.Create(0x169,1,state.ToPayload());
        Check(header.Payload.SequenceEqual(Bytes(hero,"prefix_hex").Concat(Bytes(hero,"payload_hex"))), "native current hero envelope");
        foreach(var row in data.GetProperty("publications").EnumerateArray().Skip(1))
        {
            var inventory=SacredSavedInventory.FromPayload(Bytes(row,"payload_hex"));
            var message=GoldInventoryCodec.Create(1,inventory.InventoryId,inventory);
            Check(message.Payload.SequenceEqual(Bytes(row,"prefix_hex").Concat(Bytes(row,"payload_hex"))), "actual primary/shared native envelope");
            Check(inventory.QuestCount==0, "post-entry inventory quests");
        }
        foreach(var row in data.GetProperty("items").EnumerateArray())
        {
            var stateBytes=Bytes(row,"payload_hex");
            var item=GoldObjectStateReader.ReadItem(stateBytes)!;
            Check(item.PhysicalState.BaseState.ObjectReference==row.GetProperty("reference").GetUInt32() && item.RemainingData.IsEmpty, "actual current item stream/schema");
        }
        return checks;
        void Check(bool condition,string reason){if(!condition)throw new Exception("Host current publication: "+reason);checks++;}
    }

    public static byte[] HeroWireExample(string root)
    {
        using var proof=Read(root);var row=proof.RootElement.GetProperty("publications")[0];
        return Bytes(row,"prefix_hex").Concat(Bytes(row,"payload_hex")).ToArray();
    }
    public static byte[] InventoryWireExample(string root)
    {
        using var proof=Read(root);var row=proof.RootElement.GetProperty("publications")[1];
        return Bytes(row,"prefix_hex").Concat(Bytes(row,"payload_hex")).ToArray();
    }
    public static byte[] ItemWireExample(string root)
    {
        using var proof=Read(root);var row=proof.RootElement.GetProperty("items")[0];var payload=Bytes(row,"payload_hex");
        var bytes=new byte[8+payload.Length];payload.AsSpan(4,8).CopyTo(bytes);payload.CopyTo(bytes,8);return bytes;
    }
    private static JsonDocument Read(string root)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-host-current-publication.json")));
    private static byte[] Bytes(JsonElement row,string key)=>Convert.FromHexString(row.GetProperty(key).GetString()!);
}

