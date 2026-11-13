using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class QuestItemSectorReloadChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-startup-quest-item-rebuild.json")));
        var checks=0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var itemRow = row.GetProperty("actors")[0];
            var before=Convert.FromHexString(itemRow.GetProperty("before_hex").GetString()!);
            var after=Convert.FromHexString(itemRow.GetProperty("after_hex").GetString()!);
            var basis=new GoldObjectBaseState(Word(before,12),Word(before,16),Word(before,20),BinaryPrimitives.ReadUInt16LittleEndian(before.AsSpan(24)),(int)Word(before,28),(int)Word(before,32),before[36],Word(before,40),Word(before,52),before[56],before[57],Word(before,60));
            var item=new WorldEquipmentInitialItem(new(basis,true,true),before.AsSpan(0x118,130).ToArray());
            var tile=WldxTileRecord.FromBytes(new byte[32]);
            if (Word(before,0x30)!=0) WorldScriptQuestItemSectorReload.Reinsert(item,tile,Word(before,0x30));
            var occupants=row.GetProperty("actors").EnumerateArray().Select(value=>Convert.FromHexString(value.GetProperty("before_hex").GetString()!)).ToArray();
            var tileX=SacredWorldCoordinates.WorldToTile(basis.X);var tileY=SacredWorldCoordinates.WorldToTile(basis.Y);
            var sameTile=occupants.Where(value=>SacredWorldCoordinates.WorldToTile((int)Word(value,28))==tileX && SacredWorldCoordinates.WorldToTile((int)Word(value,32))==tileY).ToArray();
            var currentHead=sameTile.Select(value=>Word(value,12)).Where(reference=>reference<basis.ObjectReference).Order().LastOrDefault();
            var head=WorldScriptQuestItemSectorReload.Reinsert(item,tile,currentHead);
            Check(WorldScriptGroundObjectSectorReload.Next(basis,tile,currentHead)==Word(after,0x30),"shared QuestItem/Container physical successor");
            Check(item.NextOnTile==Word(after,0x30),"native stale-link removal and ordered next pointer");
            var finalHead=sameTile.Max(value=>Word(value,12));
            Check(head==basis.ObjectReference && row.GetProperty("heads").EnumerateArray().Any(value=>value.GetProperty("reference").GetUInt32()==finalHead),"actual ordered tile-chain head after later reference insertion");
            Check(item.Physical.Base==basis && item.Values.AsSpan().SequenceEqual(after.AsSpan(0x118,130)),"identity, placement and all own values retained");
            Check(before.AsSpan(0,0x30).SequenceEqual(after.AsSpan(0,0x30)) && before.AsSpan(0x34).SequenceEqual(after.AsSpan(0x34)),"native entire item storage unchanged outside the next pointer");
            Check(!row.GetProperty("members").EnumerateArray().Any(value=>value.GetUInt32()==basis.ObjectReference),"QuestItem excluded from native creature sector vector");
        }
        return checks;
        void Check(bool pass,string why) { if (!pass) throw new Exception("QuestItem sector reload: "+why);checks++; }
    }
    private static uint Word(byte[] b,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
