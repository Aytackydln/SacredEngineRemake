using System;
using Sacred.Core.Pax.Hero;
using Sacred.Core.Pax.Hero.Inventory;

namespace Sacred.Engine.Multiplayer;

/// <summary>Read-only saved-record diagnostics. Decoding does not assign network references or upload a character.</summary>
internal static class GameServerSavedCharacterInspection
{
    public static string Inspect(string gameDirectory, int selection)
    {
        var archive = SacredHeroPaxArchive.ReadSelected(gameDirectory, selection);
        var decodedChunks = 0;
        EngineLog.WriteLine($"Network saved-record loading: selection {selection}, object format {archive.Header.ObjectFormatVersion}, {archive.Chunks.Length} directory entries.");
        for (var index = 0; index < archive.Chunks.Length; index++)
        {
            var chunk = archive.Chunks[index];
            if (chunk.Type == 0) continue;
            byte[] decoded;
            try { decoded = archive.DecodeChunk(index, 64 * 1024 * 1024); }
            catch (NotSupportedException)
            {
                EngineLog.WriteLine($"  Chunk {index}: unknown type 0x{chunk.Type:X}, offset {chunk.FileOffset}, declared size {chunk.DecodedSize}; metadata retained.");
                continue;
            }
            decodedChunks++;
            EngineLog.WriteLine($"  Chunk {index}: type 0x{chunk.Type:X}, decoded type 0x{SacredHeroPaxChunkTypes.GetDecodedType(chunk.Type):X}, {decoded.Length} decoded bytes.");
            if (SacredHeroPaxChunkTypes.GetDecodedType(chunk.Type) is SacredHeroPaxChunkTypes.Inventory or SacredHeroPaxChunkTypes.SharedInventory)
            {
                var inventory = SacredSavedInventory.FromPayload(decoded);
                var cellReferences = 0;
                for (var cell = 0; cell < SacredSavedInventoryLayout.CellCount; cell++)
                    if (inventory.GetCell(cell).ObjectReference != 0) cellReferences++;
                EngineLog.WriteLine($"    Inventory {inventory.InventoryId}, owner ref {inventory.OwnerReference}, grid {inventory.Width}x{inventory.Height}, flags 0x{inventory.Flags:X4}, {cellReferences} nonzero cell refs, {inventory.QuestCount} quest / {inventory.ScrollCount} scroll records, {inventory.RemainingData.Length} trailing bytes; reference reassignment pending.");
            }
        }
        var source = GameServerSavedCharacterReader.Read(archive, gameDirectory);
        var hero = source.Hero.Creature;
        var objects = 0;
        foreach (var record in source.Objects.Records) if (!record.IsEmpty) objects++;
        EngineLog.WriteLine($"Network saved hero: reference {hero.BaseState.ObjectReference}, resource {hero.BaseState.Resource}, sector {hero.BaseState.Sector}, position {hero.BaseState.X}/{hero.BaseState.Y}, {hero.Values.Count22}/{hero.Values.Count38}/{hero.Values.Count28} value records, {hero.Count9}/{hero.Count68} creature records, {hero.HeroAttachment.Length} attachment bytes, {source.Hero.RemainingData.Length} trailing bytes.");
        EngineLog.WriteLine($"Network saved object import complete: {objects} objects / {source.Objects.Count} slots, {source.Objects.RemainingData.Length} trailing bytes; semantic actor construction pending.");
        var message = $"saved selection {selection}: {decodedChunks} known chunks decoded, hero and {objects} objects read; participant reference preparation available";
        EngineLog.WriteLine($"Network saved-record loading complete: {message}.");
        return message;
    }
}
